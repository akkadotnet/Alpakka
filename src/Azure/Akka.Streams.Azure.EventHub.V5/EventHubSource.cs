//-----------------------------------------------------------------------
// <copyright file="EventHubSource.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Messaging.EventHubs.Processor;

namespace Akka.Streams.Azure.EventHub
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> for the Azure EventHub
    /// </summary>
    public class EventHubSource : 
        GraphStageWithMaterializedValue<SourceShape<ProcessContext>, EventProcessorClient>, 
        IEventHubSource<EventProcessorClient, ProcessContext, EventData>
    {
        #region Logic

        private sealed class Logic : LogicBase<EventProcessorClient, ProcessContext, EventData>
        {
            private ProcessContext? _pendingEvent;
            private TaskCompletionSource<Done>? _currentProcessingTask;

            private Action<(TaskCompletionSource<Task<Done>>, ProcessContext)>? _processCallback;

            public Logic(IEventHubSource<EventProcessorClient, ProcessContext, EventData> source, Attributes inheritedAttributes) 
                : base(source, inheritedAttributes)
            {
            }

            protected override void InitializeProcessor()
            {
                _processCallback = GetAsyncCallback<(TaskCompletionSource<Task<Done>>, ProcessContext)>(OnProcessEvents);

                Processor.PartitionClosingAsync += CloseAsync;
                Processor.PartitionInitializingAsync += OpenAsync;
                Processor.ProcessEventAsync += ProcessEventAsync;
                Processor.ProcessErrorAsync += ErrorAsync;
                
                Processor.StartProcessing();
            }
            
            private async Task ProcessEventAsync(ProcessEventArgs args)
            {
                if (args.CancellationToken.IsCancellationRequested)
                    return;
                
                if (_processCallback == null)
                    throw new ArgumentException("Logic PreStart() has not been called");
            
                var completion = new TaskCompletionSource<Task<Done>>();
                _processCallback((completion, new ProcessContext(args)));
                var processingTask = await completion.Task;
                
                // Block current call until event processing is done
                await processingTask;
            }

            private void OnProcessEvents((TaskCompletionSource<Task<Done>>, ProcessContext) args)
            {
                var (completion, context) = args;
                if(Log.IsDebugEnabled)
                {
                    Log.Debug(
                        "Event received. PartitionId: {0}, ConsumerGroup: {1}, EventHubName: {2}, Offset: {3}",
                        context.Partition.PartitionId,
                        context.Partition.ConsumerGroup,
                        context.Partition.EventHubName,
                        context.Data?.Offset);
                }

                _pendingEvent = context;
                _currentProcessingTask = new TaskCompletionSource<Done>();
                OnPull();
                completion.SetResult(_currentProcessingTask.Task);
            }
            
            public override void OnPull()
            {
                if (IsAvailable(Source.Out) && _pendingEvent != null)
                {
                    Push(Source.Out, _pendingEvent);
                    _pendingEvent = null;
                    _currentProcessingTask?.TrySetResult(Done.Instance);
                }
            }

            protected override void OnStageCompleted()
            {
                _currentProcessingTask?.TrySetResult(Done.Instance);
            }
        }
        
        #endregion

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure EventHub  
        /// </summary>
        /// <param name="factory"></param>
        /// <returns>The processor</returns>
        public static Source<ProcessContext, EventProcessorClient> Create(IProcessorFactory<EventProcessorClient> factory)
        {
            return Source.FromGraph(new EventHubSource(factory));
        }

        public IProcessorFactory<EventProcessorClient> Factory { get; }

        /// <summary>
        /// Create a new instance of the <see cref="EventHubSource"/> 
        /// </summary>
        /// <param name="factory"></param>
        private EventHubSource(IProcessorFactory<EventProcessorClient> factory)
        {
            Factory = factory;
            Shape = new SourceShape<ProcessContext>(Out);
        }

        public Outlet<ProcessContext> Out { get; } = new Outlet<ProcessContext>("EventHubSource.Out");

        public override ILogicAndMaterializedValue<EventProcessorClient> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this, inheritedAttributes); 
            return new LogicAndMaterializedValue<EventProcessorClient>(logic, logic.Processor);
        }

        public override SourceShape<ProcessContext> Shape { get; }
    }

    public class BatchedEventHubSource : 
        GraphStageWithMaterializedValue<SourceShape<BatchProcessContext>, BatchedEventProcessorClient>, 
        IEventHubSource<BatchedEventProcessorClient, BatchProcessContext, List<EventData>>
    {
        #region Logic

        private sealed class Logic : LogicBase<BatchedEventProcessorClient, BatchProcessContext, List<EventData>>
        {
            private BatchProcessContext? _pendingBatch;
            private TaskCompletionSource<Done>? _currentProcessingTask;
            
            private Action<(TaskCompletionSource<Task<Done>>, BatchProcessContext)>? _processCallback;

            public Logic(
                IEventHubSource<BatchedEventProcessorClient, BatchProcessContext, List<EventData>> source, 
                Attributes inheritedAttributes) 
                : base(source, inheritedAttributes)
            {
            }

            protected override void InitializeProcessor()
            {
                _processCallback = GetAsyncCallback<(TaskCompletionSource<Task<Done>>, BatchProcessContext)>(OnProcessEvents);

                Processor.PartitionClosingAsync += CloseAsync;
                Processor.PartitionInitializingAsync += OpenAsync;
                Processor.ProcessEventBatchAsync += ProcessEventAsync;
                Processor.ProcessErrorAsync += ErrorAsync;
                
                Processor.StartProcessing();
            }
            
            private Task ProcessEventAsync(ProcessEventBatchArgs args)
            {
                return Task.Factory.StartNew(async () =>
                {
                    if (args.CancellationToken.IsCancellationRequested)
                        return;
                
                    if(!args.HasEvents)
                        return;
                
                    if (_processCallback == null)
                        throw new ArgumentException("Logic PreStart() has not been called");
            
                    var completion = new TaskCompletionSource<Task<Done>>();
                    _processCallback((completion, new BatchProcessContext(args)));
                    var processingTask = await completion.Task;

                    // Block current call until current batch is processed
                    await processingTask;
                });
            }

            private void OnProcessEvents((TaskCompletionSource<Task<Done>>, BatchProcessContext) args)
            {
                var (completion, context) = args;
                if(Log.IsDebugEnabled)
                {
                    Log.Debug(
                        "Events received. PartitionId: {0}, ConsumerGroup: {1}, EventHubName: {2}, Event count: {3}",
                        context.Partition.PartitionId,
                        context.Partition.ConsumerGroup,
                        context.Partition.EventHubName,
                        context.Data?.Count ?? 0);
                }

                _pendingBatch = context;
                _currentProcessingTask = new TaskCompletionSource<Done>();
                OnPull();
                completion.SetResult(_currentProcessingTask.Task);
            }
            
            public override void OnPull()
            {
                if (IsAvailable(Source.Out) && _pendingBatch != null)
                {
                    Push(Source.Out, _pendingBatch);
                    _pendingBatch = null;
                    _currentProcessingTask?.TrySetResult(Done.Instance);
                }
            }

            protected override void OnStageCompleted()
            {
                _currentProcessingTask?.TrySetResult(Done.Instance);
            }
        }
        
        #endregion

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure EventHub  
        /// </summary>
        /// <param name="factory"></param>
        /// <returns>The processor</returns>
        public static Source<BatchProcessContext, BatchedEventProcessorClient> Create(
            IProcessorFactory<BatchedEventProcessorClient> factory)
        {
            return Source.FromGraph(new BatchedEventHubSource(factory));
        }

        public IProcessorFactory<BatchedEventProcessorClient> Factory { get; }

        /// <summary>
        /// Create a new instance of the <see cref="EventHubSource"/> 
        /// </summary>
        /// <param name="factory"></param>
        private BatchedEventHubSource(IProcessorFactory<BatchedEventProcessorClient> factory)
        {
            Factory = factory;
            Shape = new SourceShape<BatchProcessContext>(Out);
        }

        public Outlet<BatchProcessContext> Out { get; } =
            new Outlet<BatchProcessContext>("EventHubSource.Out");

        public override ILogicAndMaterializedValue<BatchedEventProcessorClient> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this, inheritedAttributes); 
            return new LogicAndMaterializedValue<BatchedEventProcessorClient>(logic, logic.Processor);
        }

        public override SourceShape<BatchProcessContext> Shape { get; }
    }
    
    internal interface IEventHubSource<out TClientType, TProcessContext, TData> 
        where TProcessContext: IProcessContext<TData> 
        where TData : class
        where TClientType: EventProcessor<EventProcessorPartition>
    {
        public IProcessorFactory<TClientType> Factory { get; }
        public SourceShape<TProcessContext> Shape { get; }
        public Outlet<TProcessContext> Out { get; }
    }

    #region Context classes

    public interface IProcessContext<out TData> where TData: class
    {
        public TData? Data { get; }
        public PartitionContext Partition { get; }

        public Task UpdateCheckpointAsync(CancellationToken cancellationToken = default);
    }
    
    public sealed class ProcessContext: IProcessContext<EventData>
    {
        private readonly ProcessEventArgs _args;
        private readonly Func<CancellationToken, Task> _updateCheckpointAsync;

        public ProcessContext(ProcessEventArgs eventArg)
        {
            _args = eventArg;
            _updateCheckpointAsync = _args.UpdateCheckpointAsync;
        }

        public bool HasEvent => _args.HasEvent;
        
        public PartitionContext Partition => _args.Partition;

        public EventData? Data => _args.Data;

        public Task UpdateCheckpointAsync(CancellationToken cancellationToken = default) 
            => _updateCheckpointAsync(cancellationToken);
    }
    
    public sealed class BatchProcessContext: IProcessContext<List<EventData>>
    {
        private readonly ProcessEventBatchArgs _args;
        private readonly Func<CancellationToken, Task> _updateCheckpointAsync;

        public BatchProcessContext(ProcessEventBatchArgs eventArg)
        {
            _args = eventArg;
            _updateCheckpointAsync = _args.UpdateCheckpointAsync;
        }

        public bool HasEvents => _args.HasEvents;
        
        public PartitionContext Partition => _args.Partition;

        public List<EventData>? Data => _args.Events;

        public Task UpdateCheckpointAsync(CancellationToken cancellationToken = default) 
            => _updateCheckpointAsync(cancellationToken);
    }

    #endregion
    
    internal abstract class LogicBase<TClientType, TProcessContext, TData> : OutGraphStageLogic 
        where TProcessContext: IProcessContext<TData> 
        where TData : class
        where TClientType: EventProcessor<EventProcessorPartition>
    {
        protected readonly IEventHubSource<TClientType, TProcessContext, TData> Source;
        private readonly Lazy<Decider> _decider;
        private int _partitionCount;
        
        private Action<(TaskCompletionSource<Done>, PartitionInitializingEventArgs)>? _openCallback;
        private Action<(TaskCompletionSource<Done>, PartitionClosingEventArgs)>? _closeCallback;
        private Action<(TaskCompletionSource<Done>, ProcessErrorEventArgs)>? _errorCallback;

        public TClientType Processor { get; }

        protected LogicBase(IEventHubSource<TClientType, TProcessContext, TData> source, Attributes inheritedAttributes) 
            : base(source.Shape)
        {
            Source = source;
            _decider = new Lazy<Decider>(inheritedAttributes.GetDeciderOrDefault);
            Processor = Source.Factory.CreateProcessor();
            
            SetHandler(source.Out, this);
        }

        public override void PreStart()
        {
            _openCallback = GetAsyncCallback<(TaskCompletionSource<Done>, PartitionInitializingEventArgs)>(OnOpen);
            _closeCallback = GetAsyncCallback<(TaskCompletionSource<Done>, PartitionClosingEventArgs)>(OnClose);
            _errorCallback = GetAsyncCallback<(TaskCompletionSource<Done>, ProcessErrorEventArgs)>(OnError);

            InitializeProcessor();
        }

        protected abstract void InitializeProcessor();

        protected async Task ErrorAsync(ProcessErrorEventArgs args)
        {
            if (_errorCallback == null)
                throw new ArgumentException("Logic PreStart() has not been called");
            
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
            if(IsClosed(Source.Out))
            {
                CompleteStage();
                OnStageCompleted();
                return;
            }

            OnPull();
        }

        protected async Task OpenAsync(PartitionInitializingEventArgs args)
        {
            if (_openCallback == null)
                throw new ArgumentException("Logic PreStart() has not been called");
            
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

        protected async Task CloseAsync(PartitionClosingEventArgs args)
        {
            if (_closeCallback == null)
                throw new ArgumentException("Logic PreStart() has not been called");
            
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
                OnStageCompleted();
            }
            completion.TrySetResult(Done.Instance);
        }
        
        protected virtual void OnStageCompleted() { }
    }
}
