using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Messaging.EventHubs.Processor;
using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.EventHub.Tests;

public sealed class EventHubSourceContractSpec : Akka.TestKit.Xunit2.TestKit
{
    public EventHubSourceContractSpec(ITestOutputHelper output) : base(output: output) { }

    [Fact]
    public async Task Batched_callback_waits_for_downstream_demand()
    {
        var processor = new ControlledBatchProcessor();
        var probe = BatchedEventHubSource.Create(new Factory<ControlledBatchProcessor>(processor))
            .RunWith(this.SinkProbe<BatchProcessContext>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await processor.InitializePartitionAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var processing = processor.DeliverAsync(EventHubsModelFactory.EventData(
                eventBody: BinaryData.FromString("payload"), sequenceNumber: 1, offset: 1, enqueuedTime: DateTimeOffset.UtcNow));

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            if (processing.IsCompleted)
                await processing;
            Assert.False(processing.IsCompleted, "SDK callback returned while its batch still had no downstream demand.");

            probe.Request(1);
            var context = probe.ExpectNext();
            Assert.Equal("payload", Assert.Single(context.Data!).EventBody.ToString());
            await processing.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            probe.Cancel();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_releases_pending_callbacks_and_stops_the_processor(bool batched)
    {
        if (batched)
        {
            var processor = new ControlledBatchProcessor();
            await VerifyCancellation(BatchedEventHubSource.Create(new Factory<ControlledBatchProcessor>(processor)),
                processor.State, processor.InitializePartitionAsync,
                () => processor.DeliverAsync(NewEvent()));
        }
        else
        {
            var processor = new ControlledProcessor();
            await VerifyCancellation(EventHubSource.Create(new Factory<ControlledProcessor>(processor)),
                processor.State, processor.InitializePartitionAsync,
                () => processor.DeliverAsync(NewEvent()));
        }
    }

    private async Task VerifyCancellation<T, TMat>(Source<T, TMat> source, ProcessorState state,
        Func<Task> initialize, Func<Task> deliver)
    {
        var running = source.WatchTermination(Keep.Right)
            .ToMaterialized(this.SinkProbe<T>(), Keep.Both).Run(Sys.Materializer());
        var probe = running.Item2;
        probe.EnsureSubscription();
        try
        {
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await initialize().WaitAsync(TimeSpan.FromSeconds(3));
            var callback = deliver();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            Assert.False(callback.IsCompleted);
            probe.Cancel();
            await running.Item1.WaitAsync(TimeSpan.FromSeconds(3));

            await callback.WaitAsync(TimeSpan.FromSeconds(3));
            await AwaitAssertAsync(() => Assert.Equal(1, state.AsyncStops));
            await state.Shutdown!.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, state.SyncStops);
            await initialize().WaitAsync(TimeSpan.FromSeconds(3));
            await deliver().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { probe.Cancel(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Partition_close_and_processing_failure_release_callbacks_and_stop_once(bool batched, bool fail)
    {
        var error = new InvalidOperationException("injected processing error");
        if (batched)
        {
            var processor = new ControlledBatchProcessor();
            await VerifyTermination(BatchedEventHubSource.Create(new Factory<ControlledBatchProcessor>(processor)),
                processor.State, processor.InitializePartitionAsync, () => processor.DeliverAsync(NewEvent()),
                () => fail ? processor.ReportErrorAsync(error) : processor.ClosePartitionAsync(), fail ? error : null);
        }
        else
        {
            var processor = new ControlledProcessor();
            await VerifyTermination(EventHubSource.Create(new Factory<ControlledProcessor>(processor)),
                processor.State, processor.InitializePartitionAsync, () => processor.DeliverAsync(NewEvent()),
                () => fail ? processor.ReportErrorAsync(error) : processor.ClosePartitionAsync(), fail ? error : null);
        }
    }

    private async Task VerifyTermination<T, TMat>(Source<T, TMat> source, ProcessorState state,
        Func<Task> initialize, Func<Task> deliver, Func<Task> terminate, Exception? error)
    {
        var probe = source.RunWith(this.SinkProbe<T>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await initialize().WaitAsync(TimeSpan.FromSeconds(3));
            var pending = deliver();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            Assert.False(pending.IsCompleted);
            var terminalCallback = terminate();
            if (error is null)
                probe.ExpectComplete();
            else
                Assert.Same(error, probe.ExpectError());

            await terminalCallback.WaitAsync(TimeSpan.FromSeconds(3));
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            await AwaitAssertAsync(() => Assert.Equal(1, state.AsyncStops));
            await state.Shutdown!.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, state.SyncStops);
        }
        finally { probe.Cancel(); }
    }

    private static EventData NewEvent() => EventHubsModelFactory.EventData(
        eventBody: BinaryData.FromString("payload"), sequenceNumber: 1, offset: 1, enqueuedTime: DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partition_cancellation_releases_the_callback_without_emitting_its_event(bool batched)
    {
        if (batched)
        {
            var processor = new ControlledBatchProcessor();
            await VerifyPartitionCancellation(BatchedEventHubSource.Create(new Factory<ControlledBatchProcessor>(processor)),
                processor.State, processor.InitializePartitionAsync, processor.DeliverWithTokenAsync);
        }
        else
        {
            var processor = new ControlledProcessor();
            await VerifyPartitionCancellation(EventHubSource.Create(new Factory<ControlledProcessor>(processor)),
                processor.State, processor.InitializePartitionAsync, processor.DeliverWithTokenAsync);
        }
    }

    private async Task VerifyPartitionCancellation<T, TMat>(Source<T, TMat> source, ProcessorState state,
        Func<Task> initialize, Func<EventData, CancellationToken, Task> deliver)
    {
        var probe = source.RunWith(this.SinkProbe<T>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await initialize().WaitAsync(TimeSpan.FromSeconds(3));
            using var partition = new CancellationTokenSource();
            var canceled = deliver(NewEvent(), partition.Token);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            Assert.False(canceled.IsCompleted);
            partition.Cancel();
            await canceled.WaitAsync(TimeSpan.FromSeconds(3));
            probe.Request(1).ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            var next = deliver(NewEvent(), CancellationToken.None);
            probe.ExpectNext();
            await next.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, state.AsyncStops);
        }
        finally { probe.Cancel(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_failure_keeps_the_original_error_and_stops_the_processor(bool batched)
    {
        var error = new InvalidOperationException("injected startup failure");
        if (batched)
        {
            var processor = new ControlledBatchProcessor();
            processor.State.StartFailure = error;
            await VerifyStartupFailure(BatchedEventHubSource.Create(new Factory<ControlledBatchProcessor>(processor)), processor.State, error);
        }
        else
        {
            var processor = new ControlledProcessor();
            processor.State.StartFailure = error;
            await VerifyStartupFailure(EventHubSource.Create(new Factory<ControlledProcessor>(processor)), processor.State, error);
        }
    }

    [Fact]
    public async Task Batch_checkpoint_uses_the_highest_offset_and_its_sequence_number()
    {
        var processor = new ControlledBatchProcessor();
        var probe = BatchedEventHubSource.Create(new Factory<ControlledBatchProcessor>(processor))
            .RunWith(this.SinkProbe<BatchProcessContext>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await processor.InitializePartitionAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var delivered = processor.DeliverAsync(Event(50, 5), Event(90, 9), Event(70, 7));
            var context = probe.Request(1).ExpectNext();
            using var checkpoint = new CancellationTokenSource();
            await context.UpdateCheckpointAsync(checkpoint.Token);
            await delivered.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(("0", 90L, (long?)9, checkpoint.Token), Assert.Single(processor.Checkpoints));
        }
        finally { probe.Cancel(); }

        static EventData Event(long offset, long sequence) => EventHubsModelFactory.EventData(
            eventBody: BinaryData.FromString("payload"), sequenceNumber: sequence, offset: offset, enqueuedTime: DateTimeOffset.UtcNow);
    }

    private async Task VerifyStartupFailure<T, TMat>(Source<T, TMat> source, ProcessorState state, Exception error)
    {
        var probe = source.RunWith(this.SinkProbe<T>(), Sys.Materializer());
        probe.EnsureSubscription();
        Assert.Same(error, probe.ExpectError());
        await AwaitAssertAsync(() => Assert.Equal(1, state.AsyncStops));
        await state.Shutdown!.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, state.SyncStops);
    }

    private sealed class Factory<T>(T processor) : IProcessorFactory<T>
        where T : EventProcessor<EventProcessorPartition>
    {
        public T CreateProcessor() => processor;
    }

    private sealed class ControlledBatchProcessor : BatchedEventProcessorClient
    {
        private readonly EventProcessorPartition _partition = new TestPartition();
        public ProcessorState State { get; } = new();
        public TaskCompletionSource Started => State.Started;
        public List<(string, long, long?, CancellationToken)> Checkpoints { get; } = [];

        public ControlledBatchProcessor() : base(new BlobContainerClient("UseDevelopmentStorage=true", "checkpoints"),
            "test-group", "Endpoint=sb://unit.test/;SharedAccessKeyName=test;SharedAccessKey=" + Convert.ToBase64String(new byte[32]),
            "test-hub") { }

        public override void StartProcessing(CancellationToken cancellationToken = default) => State.Start();
        public override void StopProcessing(CancellationToken cancellationToken = default) => State.RecordSyncStop();
        public override Task StopProcessingAsync(CancellationToken cancellationToken = default) => State.StopAsync(
            () => base.OnPartitionProcessingStoppedAsync(_partition, ProcessingStoppedReason.Shutdown, CancellationToken.None));
        public Task InitializePartitionAsync() => State.Record(base.OnInitializingPartitionAsync(_partition, CancellationToken.None));
        public Task DeliverAsync(params EventData[] events) => State.Record(base.OnProcessingEventBatchAsync(events, _partition, CancellationToken.None));
        public Task DeliverWithTokenAsync(EventData data, CancellationToken token) => State.Record(base.OnProcessingEventBatchAsync([data], _partition, token));
        public Task ClosePartitionAsync() => State.Record(base.OnPartitionProcessingStoppedAsync(_partition, ProcessingStoppedReason.OwnershipLost, CancellationToken.None));
        public Task ReportErrorAsync(Exception error) => State.Record(base.OnProcessingErrorAsync(error, _partition, "test", CancellationToken.None));
        protected override Task UpdateCheckpointAsync(string partitionId, long offset, long? sequenceNumber, CancellationToken token)
        {
            Checkpoints.Add((partitionId, offset, sequenceNumber, token));
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledProcessor : EventProcessorClient
    {
        private readonly EventProcessorPartition _partition = new TestPartition();
        public ProcessorState State { get; } = new();
        public ControlledProcessor() : base(new BlobContainerClient("UseDevelopmentStorage=true", "checkpoints"),
            "test-group", "Endpoint=sb://unit.test/;SharedAccessKeyName=test;SharedAccessKey=" + Convert.ToBase64String(new byte[32]),
            "test-hub") { }
        public override void StartProcessing(CancellationToken cancellationToken = default) => State.Start();
        public override void StopProcessing(CancellationToken cancellationToken = default) => State.RecordSyncStop();
        public override Task StopProcessingAsync(CancellationToken cancellationToken = default) => State.StopAsync(
            () => base.OnPartitionProcessingStoppedAsync(_partition, ProcessingStoppedReason.Shutdown, CancellationToken.None));
        public Task InitializePartitionAsync() => State.Record(base.OnInitializingPartitionAsync(_partition, CancellationToken.None));
        public Task DeliverAsync(params EventData[] events) => State.Record(base.OnProcessingEventBatchAsync(events, _partition, CancellationToken.None));
        public Task DeliverWithTokenAsync(EventData data, CancellationToken token) => State.Record(base.OnProcessingEventBatchAsync([data], _partition, token));
        public Task ClosePartitionAsync() => State.Record(base.OnPartitionProcessingStoppedAsync(_partition, ProcessingStoppedReason.OwnershipLost, CancellationToken.None));
        public Task ReportErrorAsync(Exception error) => State.Record(base.OnProcessingErrorAsync(error, _partition, "test", CancellationToken.None));
    }

    private sealed class ProcessorState
    {
        private readonly ConcurrentQueue<Task> _callbacks = new();
        private int _syncStops;
        private int _asyncStops;
        public int SyncStops => Volatile.Read(ref _syncStops);
        public int AsyncStops => Volatile.Read(ref _asyncStops);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Shutdown { get; private set; }
        public Exception? StartFailure { get; set; }
        public void Start()
        {
            if (StartFailure is not null)
                throw StartFailure;
            Started.TrySetResult();
        }
        public Task Record(Task callback) { _callbacks.Enqueue(callback); return callback; }
        public void RecordSyncStop() => Interlocked.Increment(ref _syncStops);
        public Task StopAsync(Func<Task> closing)
        {
            Interlocked.Increment(ref _asyncStops);
            Record(closing());
            return Shutdown = Task.WhenAll(_callbacks);
        }
    }

    private sealed class TestPartition : EventProcessorPartition
    {
        public TestPartition() => PartitionId = "0";
    }
}
