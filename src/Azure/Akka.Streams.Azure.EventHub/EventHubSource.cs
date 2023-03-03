using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Azure.Utils;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Util;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;

namespace Akka.Streams.Azure.EventHub
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> for the Azure EventHub
    /// that materialized into an <see cref="IEventProcessor"/>
    /// </summary>
    public class EventHubSource : GraphStageWithMaterializedValue<SourceShape<Tuple<PartitionContext, EventData>>, IEventProcessor>
    {
        #region Logic

        private sealed class ProcessContext
        {
            public ProcessContext(TaskCompletionSource<NotUsed> completion, PartitionContext context,
                IEnumerable<EventData> events)
            {
                Completion = completion;
                Context = context;
                Events = events;
            }

            public TaskCompletionSource<NotUsed> Completion { get; }

            public PartitionContext Context { get; }

            public IEnumerable<EventData> Events { get; }
        }

        private sealed class Logic : GraphStageLogic, IEventProcessor
        {
            private readonly AtomicBoolean _started = new AtomicBoolean();
            private readonly EventHubSource _source;
            private Action<(TaskCompletionSource<NotUsed>, PartitionContext)> _openCallback;
            private Action<(TaskCompletionSource<NotUsed>, PartitionContext, CloseReason)> _closeCallback;
            private Action<ProcessContext> _processCallback;
            private Action<(TaskCompletionSource<NotUsed>, PartitionContext, Exception)> _errorCallback;
            private TaskCompletionSource<NotUsed> _pendingCompletion;
            private Queue<EventData> _pendingEvents;
            private PartitionContext _currentContext;
            private int _partitionCount;
            private readonly Decider _decider;

            public Logic(EventHubSource source, Attributes inheritedAttributes) : base(source.Shape)
            {
                _source = source;
                _decider = inheritedAttributes.GetDeciderOrDefault();
                SetHandler(source.Out, TryPush);
            }

            public override void PreStart()
            {
                _openCallback = GetAsyncCallback<(TaskCompletionSource<NotUsed>, PartitionContext)>(OnOpen);
                _closeCallback = GetAsyncCallback<(TaskCompletionSource<NotUsed>, PartitionContext, CloseReason)>(OnClose);
                _processCallback = GetAsyncCallback<ProcessContext>(OnProcessEvents);
                _errorCallback = GetAsyncCallback<(TaskCompletionSource<NotUsed>, PartitionContext, Exception)>(OnError);
                _started.CompareAndSet(false, true);
            }

            public async Task OpenAsync(PartitionContext context)
            {
                // It's possible that PreStart wasn't called before this code executes
                while (!_started)
                    await Task.Delay(500);

                var completion = new TaskCompletionSource<NotUsed>();
                _openCallback((completion, context));
                await completion.Task;
            }

            private void OnOpen((TaskCompletionSource<NotUsed>, PartitionContext) args)
            {
                var (completion, context) = args;
                
                if(Log.IsDebugEnabled)
                    Log.Debug("Partition initializing. Partition: [{0}]", context.ToString());
                
                // We need to count the partitions to close the stage only on the last close call,
                // otherwise further calls to the _closeCallback wouldn't be handled because they
                // are moved to DeadLetters and the close task is never completed
                _partitionCount++;
                completion.TrySetResult(NotUsed.Instance);
            }

            public async Task CloseAsync(PartitionContext context, CloseReason reason)
            {
                var completion = new TaskCompletionSource<NotUsed>();
                _closeCallback((completion, context, reason));
                await completion.Task;

                if (_source._createCheckpointOnClose)
                    await context.CheckpointAsync();
            }

            private void OnClose((TaskCompletionSource<NotUsed>, PartitionContext, CloseReason) args)
            {
                var (completion, context, reason) = args;
                if(Log.IsDebugEnabled)
                    Log.Debug("Partition closing. Partition: [{0}], Reason: [{1}]", context.ToString(), reason);
                
                if(--_partitionCount == 0)
                    CompleteStage();
                completion.TrySetResult(NotUsed.Instance);
            }

            public async Task ProcessErrorAsync(PartitionContext context, Exception error)
            {
                var completion = new TaskCompletionSource<NotUsed>();
                _errorCallback((completion, context, error));
                await completion.Task;
            }

            private void OnError((TaskCompletionSource<NotUsed>, PartitionContext, Exception) args)
            {
                var (completion, context, cause) = args;
                
                Log.Error(cause, "Error while processing event. Partition: [{0}]", context.ToString());
                switch (_decider(cause))
                {
                    case Directive.Stop:
                        // Throw
                        completion.TrySetException(cause);
                        FailStage(cause);
                        break;
                    case Directive.Resume:
                    case Directive.Restart:
                        // Push the next element or complete
                        PushOrComplete();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public async Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
            {
                var completion = new TaskCompletionSource<NotUsed>();
                _processCallback(new ProcessContext(completion, context, messages));
                await completion.Task;

                if (!completion.Task.IsCanceled && !completion.Task.IsFaulted && _source._createCheckpointForEveryBatch)
                    await context.CheckpointAsync();
            }
            
            private void OnProcessEvents(ProcessContext context)
            {
                // ProcessEventsAsync is only called when the previous task is completed, 
                // therefore we can simply replace the previous values
                _pendingCompletion = context.Completion;
                _pendingEvents = new Queue<EventData>(context.Events);
                _currentContext = context.Context;

                TryPush();
            }

            private void PushOrComplete()
            {
                if (IsClosed(_source.Out))
                {
                    _pendingEvents.Clear();
                    _pendingCompletion.TrySetCanceled();
                    CompleteStage();
                }
                else
                {
                    TryPush();
                }
            }
            
            private void TryPush()
            {
                // Something happened to the last batch, abort the send
                if (_pendingEvents.Count > 0 && _pendingCompletion.Task.IsCanceled || _pendingCompletion.Task.IsFaulted)
                {
                    _pendingEvents.Clear();
                    return;
                }
                
                // Wait for new messages
                if(_pendingEvents == null || _pendingEvents.Count == 0)
                    return;

                if (!IsAvailable(_source.Out)) 
                    return;
                
                Push(_source.Out, Tuple.Create(_currentContext, _pendingEvents.Dequeue()));

                // We have processed all messages so we can handle more
                if (_pendingEvents.Count == 0)
                    _pendingCompletion.TrySetResult(NotUsed.Instance);
            }
        }

        #endregion

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure EventHub  
        /// </summary>
        /// <param name="createCheckpointOnClose">Creates a checkpoint when the processor is closed, if set to true</param>
        /// <param name="createCheckpointForEveryBatch">Creates a checkpoint for every batch of messages that are received from the EventHub, if set to true</param>
        /// <returns>The processor</returns>
        public static Source<Tuple<PartitionContext, EventData>, IEventProcessor> Create(bool createCheckpointOnClose = true, bool createCheckpointForEveryBatch = false)
        {
            return Source.FromGraph(new EventHubSource(createCheckpointOnClose, createCheckpointForEveryBatch));
        }

        private readonly bool _createCheckpointOnClose;
        private readonly bool _createCheckpointForEveryBatch;

        /// <summary>
        /// Create a new instance of the <see cref="EventHubSource"/> 
        /// </summary>
        /// <param name="createCheckpointOnClose">Creates a checkpoint when the processor is closed if set to true</param>
        /// <param name="createCheckpointForEveryBatch">Creates a checkpoint for every batch of messages that are received from the EventHub, if set to true</param>
        public EventHubSource(bool createCheckpointOnClose = true, bool createCheckpointForEveryBatch = false)
        {
            _createCheckpointOnClose = createCheckpointOnClose;
            _createCheckpointForEveryBatch = createCheckpointForEveryBatch;
            Shape = new SourceShape<Tuple<PartitionContext, EventData>>(Out);
        }

        public Outlet<Tuple<PartitionContext, EventData>> Out { get; } =
            new Outlet<Tuple<PartitionContext, EventData>>("EventHubSource.Out");

        public override SourceShape<Tuple<PartitionContext, EventData>> Shape { get; }

        public override ILogicAndMaterializedValue<IEventProcessor> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var logic = new Logic(this, inheritedAttributes);
            return new LogicAndMaterializedValue<IEventProcessor>(logic, logic);
        }
    }
}
