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
        private class Holder
        {
            public Holder(ProcessContext context, TaskCompletionSource<Done> completion)
            {
                TaskCompletionSource = completion;
                ProcessContext = context;
            }

            public ProcessContext ProcessContext { get; }
            public TaskCompletionSource<Done> TaskCompletionSource { get; }
        }
        
        #region Logic
        private sealed class Logic : LogicBase<EventProcessorClient, ProcessContext, EventData>
        {
            private readonly Queue<Holder> _pendingQueue = new ();
            private Action<(TaskCompletionSource<Done>, ProcessContext)>? _processCallback;
            
            public Logic(IEventHubSource<EventProcessorClient, ProcessContext, EventData> source, Attributes inheritedAttributes) 
                : base(source, inheritedAttributes)
            {
            }
            
            protected override void InitializeProcessor()
            {
                _processCallback = GetAsyncCallback<(TaskCompletionSource<Done>, ProcessContext)>(OnProcessEvents);
                
                Processor.PartitionClosingAsync += CloseAsync;
                Processor.PartitionInitializingAsync += OpenAsync;
                Processor.ProcessEventAsync += ProcessEventAsync;
                Processor.ProcessErrorAsync += ErrorAsync;
                
                Processor.StartProcessing(CompletionToken);
            }
            
            private Task ProcessEventAsync(ProcessEventArgs args)
            {
                if (_processCallback == null)
                    throw new ArgumentException("Logic PreStart() has not been called");

                return DispatchAsync(_processCallback, new ProcessContext(args), args.CancellationToken);
            }

            private void OnProcessEvents((TaskCompletionSource<Done>, ProcessContext) args)
            {
                var (completion, context) = args;
                if (completion.Task.IsCompleted)
                    return;
                if(Log.IsDebugEnabled)
                {
                    Log.Debug(
                        "Event received. PartitionId: {0}, ConsumerGroup: {1}, EventHubName: {2}, Offset: {3}",
                        context.Partition.PartitionId,
                        context.Partition.ConsumerGroup,
                        context.Partition.EventHubName,
                        context.Data?.Offset.ToString() ?? "null");
                }

                var holder = new Holder(context, completion);
                _pendingQueue.Enqueue(holder);
                OnPull();
            }

            public override void OnPull()
            {
                while (IsAvailable(Source.Out) && _pendingQueue.Count > 0)
                {
                    var holder = _pendingQueue.Dequeue();
                    if (holder.ProcessContext.CancellationToken.IsCancellationRequested)
                        continue;
                    Push(Source.Out, holder.ProcessContext);
                    holder.TaskCompletionSource.TrySetResult(Done.Instance);
                }
            }

            protected override void OnStageCompleted()
            {
                _pendingQueue.Clear();
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

        public Outlet<ProcessContext> Out { get; } = new ("EventHubSource.Out");

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
        private class Holder
        {
            public Holder(BatchProcessContext context, TaskCompletionSource<Done> completion)
            {
                TaskCompletionSource = completion;
                ProcessContext = context;
            }

            public BatchProcessContext ProcessContext { get; }
            public TaskCompletionSource<Done> TaskCompletionSource { get; }
        }
        
        #region Logic
        private sealed class Logic : LogicBase<BatchedEventProcessorClient, BatchProcessContext, List<EventData>>
        {
            private readonly Queue<Holder> _pendingQueue = new ();
            
            private Action<(TaskCompletionSource<Done>, BatchProcessContext)>? _processCallback;

            public Logic(
                IEventHubSource<BatchedEventProcessorClient, BatchProcessContext, List<EventData>> source, 
                Attributes inheritedAttributes) 
                : base(source, inheritedAttributes)
            {
            }

            protected override void InitializeProcessor()
            {
                _processCallback = GetAsyncCallback<(TaskCompletionSource<Done>, BatchProcessContext)>(OnProcessEvents);
                
                Processor.PartitionClosingAsync += CloseAsync;
                Processor.PartitionInitializingAsync += OpenAsync;
                Processor.ProcessEventBatchAsync += ProcessEventAsync;
                Processor.ProcessErrorAsync += ErrorAsync;
                
                Processor.StartProcessing(CompletionToken);
            }
            
            private Task ProcessEventAsync(ProcessEventBatchArgs args)
            {
                if (!args.HasEvents)
                    return Task.CompletedTask;
                if (_processCallback == null)
                    throw new ArgumentException("Logic PreStart() has not been called");

                return DispatchAsync(_processCallback, new BatchProcessContext(args), args.CancellationToken);
            }

            private void OnProcessEvents((TaskCompletionSource<Done>, BatchProcessContext) args)
            {
                var (completion, context) = args;
                if (completion.Task.IsCompleted)
                    return;
                if(Log.IsDebugEnabled)
                {
                    Log.Debug(
                        "Events received. PartitionId: {0}, ConsumerGroup: {1}, EventHubName: {2}, Event count: {3}",
                        context.Partition.PartitionId,
                        context.Partition.ConsumerGroup,
                        context.Partition.EventHubName,
                        context.Data?.Count ?? 0);
                }

                var holder = new Holder(context, completion);
                _pendingQueue.Enqueue(holder);
                OnPull();
            }
            
            public override void OnPull()
            {
                while (IsAvailable(Source.Out) && _pendingQueue.Count > 0)
                {
                    var holder = _pendingQueue.Dequeue();
                    if (holder.ProcessContext.CancellationToken.IsCancellationRequested)
                        continue;
                    Push(Source.Out, holder.ProcessContext);
                    holder.TaskCompletionSource.TrySetResult(Done.Instance);
                }
            }

            protected override void OnStageCompleted()
            {
                _pendingQueue.Clear();
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
        public CancellationToken CancellationToken { get; }

        public Task UpdateCheckpointAsync(CancellationToken cancellationToken = default);
    }
    
    public sealed class ProcessContext: IProcessContext<EventData>
    {
        private readonly Func<CancellationToken, Task> _updateCheckpointAsync;

        public ProcessContext(ProcessEventArgs eventArg)
        {
            _updateCheckpointAsync = eventArg.UpdateCheckpointAsync;
            Partition = eventArg.Partition;
            Data = eventArg.Data;
            HasEvent = eventArg.HasEvent;
            CancellationToken = eventArg.CancellationToken;
        }

        public bool HasEvent { get; }
        
        public PartitionContext Partition { get; }

        public EventData? Data { get; }
        
        public CancellationToken CancellationToken { get; }

        public Task UpdateCheckpointAsync(CancellationToken cancellationToken = default) 
            => _updateCheckpointAsync(cancellationToken);
    }
    
    public sealed class BatchProcessContext: IProcessContext<List<EventData>>
    {
        private readonly Func<CancellationToken, Task> _updateCheckpointAsync;

        public BatchProcessContext(ProcessEventBatchArgs eventArg)
        {
            _updateCheckpointAsync = eventArg.UpdateCheckpointAsync;
            HasEvents = eventArg.HasEvents;
            Partition = eventArg.Partition;
            Data = eventArg.Events;
            CancellationToken = eventArg.CancellationToken;
        }

        public bool HasEvents { get; }
        
        public PartitionContext Partition { get; }

        public List<EventData>? Data { get; }
        
        public CancellationToken CancellationToken { get; }

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
        private readonly CancellationTokenSource _completionCts;
        protected readonly CancellationToken CompletionToken;
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
            _completionCts = new CancellationTokenSource();
            CompletionToken = _completionCts.Token;
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

        // SDK callbacks can arrive concurrently with stage termination. Releasing their
        // promises on cancellation also lets StopProcessingAsync finish partition shutdown.
        protected async Task DispatchAsync<T>(Action<(TaskCompletionSource<Done>, T)> callback,
            T args, CancellationToken cancellationToken = default)
        {
            if (CompletionToken.IsCancellationRequested || cancellationToken.IsCancellationRequested)
                return;

            var completion = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (CompletionToken.Register(() => completion.TrySetResult(Done.Instance)))
            using (cancellationToken.Register(() => completion.TrySetResult(Done.Instance)))
            {
                callback((completion, args));
                await completion.Task.ConfigureAwait(false);
            }
        }

        protected Task ErrorAsync(ProcessErrorEventArgs args)
        {
            if (_errorCallback == null)
                throw new ArgumentException("Logic PreStart() has not been called");
            
            return DispatchAsync(_errorCallback, args);
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
                return;
            }

            OnPull();
        }

        protected Task OpenAsync(PartitionInitializingEventArgs args)
        {
            if (_openCallback == null)
                throw new ArgumentException("Logic PreStart() has not been called");
            
            return DispatchAsync(_openCallback, args);
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

        protected Task CloseAsync(PartitionClosingEventArgs args)
        {
            if (_closeCallback == null)
                throw new ArgumentException("Logic PreStart() has not been called");
            
            return DispatchAsync(_closeCallback, args);
        }

        private void OnClose((TaskCompletionSource<Done>, PartitionClosingEventArgs) args)
        {
            var (completion, eventArgs) = args;
            if(Log.IsDebugEnabled)
                Log.Debug(
                    "Partition closing. PartitionId: {0}, Reason: {1}",
                    eventArgs.PartitionId,
                    eventArgs.Reason);
            
            completion.TrySetResult(Done.Instance);
            if(--_partitionCount == 0)
            {
                if(Log.IsDebugEnabled)
                    Log.Debug("Source stopped, no partition left to process.");
                CompleteStage();
            }
        }

        public override void PostStop()
        {
            _completionCts.Cancel();
            OnStageCompleted();
            // Never block the interpreter on SDK shutdown: partition-close handlers may
            // themselves be waiting for this stage's callbacks to finish.
            _ = StopProcessorAsync(Log);
            base.PostStop();
        }

        private async Task StopProcessorAsync(ILoggingAdapter log)
        {
            try
            {
                await Processor.StopProcessingAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error stopping the Event Hubs processor after source termination.");
            }
            finally
            {
                _completionCts.Dispose();
            }
        }

        protected virtual void OnStageCompleted() { }
    }
}
