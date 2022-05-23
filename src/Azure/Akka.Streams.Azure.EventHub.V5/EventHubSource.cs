using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;

namespace Akka.Streams.Azure.EventHub.V5
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> for the Azure EventHub
    /// </summary>
    public class EventHubSource : GraphStageWithMaterializedValue<SourceShape<EventHubSource.ProcessContext>, EventProcessorClient>
    {
        #region Logic

        public sealed class ProcessContext
        {
            private readonly ProcessEventArgs _args;
            private readonly Func<CancellationToken, Task> _updateCheckpointAsync;

            public ProcessContext(ProcessEventArgs eventArg)
            {
                _args = eventArg;
                _updateCheckpointAsync = _args.UpdateCheckpointAsync;
            }

            public PartitionContext Partition => _args.Partition;

            public EventData Event => _args.Data;

            public Task UpdateCheckpointAsync(CancellationToken cancellationToken = default) 
                => _updateCheckpointAsync(cancellationToken);
        }

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly EventHubSource _source;
            private readonly Queue<ProcessContext> _pendingEvents;
            private readonly Lazy<Decider> _decider;
            private int _partitionCount;
            
            private Action<(TaskCompletionSource<Done>, PartitionInitializingEventArgs)> _openCallback;
            private Action<(TaskCompletionSource<Done>, PartitionClosingEventArgs)> _closeCallback;
            private Action<(TaskCompletionSource<Done>, ProcessContext)> _processCallback;
            private Action<(TaskCompletionSource<Done>, ProcessErrorEventArgs)> _errorCallback;

            public EventProcessorClient Processor { get; }
            
            public Logic(EventHubSource source, Attributes inheritedAttributes) : base(source.Shape)
            {
                _source = source;
                _decider = new Lazy<Decider>(() =>
                {
                    var strategy = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return strategy != null ? strategy.Decider : Deciders.StoppingDecider;
                });
                
                _pendingEvents = new Queue<ProcessContext>();
                Processor = _source._factory.CreateProcessor();
                
                SetHandler(source.Out, this);
            }

            public override void PreStart()
            {
                _openCallback = GetAsyncCallback<(TaskCompletionSource<Done>, PartitionInitializingEventArgs)>(OnOpen);
                _closeCallback = GetAsyncCallback<(TaskCompletionSource<Done>, PartitionClosingEventArgs)>(OnClose);
                _processCallback = GetAsyncCallback<(TaskCompletionSource<Done>, ProcessContext)>(OnProcessEvents);
                _errorCallback = GetAsyncCallback<(TaskCompletionSource<Done>, ProcessErrorEventArgs)>(OnError);

                Processor.PartitionClosingAsync += CloseAsync;
                Processor.PartitionInitializingAsync += OpenAsync;
                Processor.ProcessEventAsync += ProcessEventAsync;
                Processor.ProcessErrorAsync += ErrorAsync;

                Processor.StartProcessing();
            }

            private async Task ErrorAsync(ProcessErrorEventArgs args)
            {
                var completion = new TaskCompletionSource<Done>();
                _errorCallback((completion, args));
                await completion.Task;
            }
            
            private void OnError((TaskCompletionSource<Done>, ProcessErrorEventArgs) args)
            {
                var (completion, eventArgs) = args;
                if(Log.IsWarningEnabled)
                    Log.Warning(
                        eventArgs.Exception, 
                        "Error processing event. PartitionId: {0}, Operation: {1}", 
                        eventArgs.PartitionId, 
                        eventArgs.Operation);
                
                ErrorHandler(eventArgs.Exception);
                completion.TrySetResult(Done.Instance);
            }
            
            private void ErrorHandler(Exception ex)
            {
                switch (_decider.Value(ex))
                {
                    case Directive.Stop:
                        FailStage(ex);
                        break;
                    case Directive.Restart:
                    case Directive.Resume:
                        ResumeOrComplete();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private void ResumeOrComplete()
            {
                if(IsClosed(_source.Out))
                {
                    CompleteStage();
                    return;
                }

                OnPull();
            }

            private async Task OpenAsync(PartitionInitializingEventArgs args)
            {
                var completion = new TaskCompletionSource<Done>();
                _openCallback((completion, args));
                await completion.Task;
            }

            private void OnOpen((TaskCompletionSource<Done>, PartitionInitializingEventArgs) args)
            {
                var (completion, eventArgs) = args;
                if(Log.IsDebugEnabled)
                    Log.Debug(
                        "Partition initializing. PartitionId: {0}, EventPosition: {1}", 
                        eventArgs.PartitionId,
                        eventArgs.DefaultStartingPosition);
                
                // We need to count the partitions to close the stage only on the last close call,
                // otherwise further calls to the _closeCallback wouldn't be handled because they
                // are moved to DeadLetters and the close task is never completed
                _partitionCount++;
                completion.TrySetResult(Done.Instance);
            }

            private async Task CloseAsync(PartitionClosingEventArgs args)
            {
                var completion = new TaskCompletionSource<Done>();
                _closeCallback((completion, args));
                await completion.Task;
            }

            private void OnClose((TaskCompletionSource<Done>, PartitionClosingEventArgs) args)
            {
                var (completion, eventArgs) = args;
                if(Log.IsDebugEnabled)
                    Log.Debug(
                        "Partition closing. PartitionId: {0}, Reason: {1}",
                        eventArgs.PartitionId,
                        eventArgs.Reason);
                
                if(--_partitionCount == 0)
                {
                    if(Log.IsDebugEnabled)
                        Log.Debug("Source stopped, no partition left to process.");
                    CompleteStage();
                }
                completion.TrySetResult(Done.Instance);
            }

            private async Task ProcessEventAsync(ProcessEventArgs args)
            {
                if (args.CancellationToken.IsCancellationRequested)
                    return;
                    
                if (args.HasEvent)
                {
                    var completion = new TaskCompletionSource<Done>();
                    _processCallback((completion, new ProcessContext(args)));
                    await completion.Task;
                }
            }

            private void OnProcessEvents((TaskCompletionSource<Done>, ProcessContext) args)
            {
                var (completion, context) = args;
                if(Log.IsDebugEnabled)
                {
                    Log.Debug(
                        "Event received. PartitionId: {0}, ConsumerGroup: {1}, EventHubName: {2}, Offset: {3}",
                        context.Partition.PartitionId,
                        context.Partition.ConsumerGroup,
                        context.Partition.EventHubName,
                        context.Event.Offset);
                }

                _pendingEvents.Enqueue(context);
                OnPull();
                completion.SetResult(Done.Instance);
            }

            public override void OnPull()
            {
                if (IsAvailable(_source.Out) && _pendingEvents.Count > 0)
                {
                    Push(_source.Out, _pendingEvents.Dequeue());
                }
            }
        }

        #endregion

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure EventHub  
        /// </summary>
        /// <param name="factory"></param>
        /// <returns>The processor</returns>
        public static Source<ProcessContext, EventProcessorClient> Create(IProcessorFactory factory)
        {
            return Source.FromGraph(new EventHubSource(factory));
        }

        private readonly IProcessorFactory _factory;

        /// <summary>
        /// Create a new instance of the <see cref="EventHubSource"/> 
        /// </summary>
        /// <param name="factory"></param>
        public EventHubSource(IProcessorFactory factory)
        {
            _factory = factory;
            Shape = new SourceShape<ProcessContext>(Out);
        }

        public Outlet<ProcessContext> Out { get; } =
            new Outlet<ProcessContext>("EventHubSource.Out");

        public override ILogicAndMaterializedValue<EventProcessorClient> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this, inheritedAttributes); 
            return new LogicAndMaterializedValue<EventProcessorClient>(logic, logic.Processor);
        }

        public override SourceShape<ProcessContext> Shape { get; }
    }
}
