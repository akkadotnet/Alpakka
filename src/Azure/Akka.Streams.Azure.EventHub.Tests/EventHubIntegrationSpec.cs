using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.EventHub.Tests;

public sealed class EventHubIntegrationSpec(ITestOutputHelper output) : EventHubSpecBase(output)
{
    [Fact]
    public async Task Sink_sends_exact_payloads_and_preserves_the_borrowed_producer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var producer = new EventHubProducerClient(ConnectionString, "orders");
        await using var reader = new EventHubConsumerClient(EventHubConsumerClient.DefaultConsumerGroupName, ConnectionString, "orders");
        var expected = new[] { "first", "café", "last" };

        await Source.From(expected).Select(value => new EventData(value)).Grouped(2)
            .RunWith(EventHubSink.Create(producer), Sys.Materializer()).WaitAsync(deadline.Token);

        var actual = new List<string>();
        await foreach (var received in reader.ReadEventsAsync(startReadingAtEarliestEvent: true,
                           new ReadEventOptions { MaximumWaitTime = TimeSpan.FromSeconds(1) }, deadline.Token))
        {
            if (received.Data is null)
                continue;
            actual.Add(received.Data.EventBody.ToString());
            if (actual.Count == expected.Length)
                break;
        }

        Assert.Equal(expected.OrderBy(value => value), actual.OrderBy(value => value));
        Assert.False(producer.IsClosed);
        var ids = await reader.GetPartitionIdsAsync(deadline.Token);
        Assert.Equal(2, ids.Length);
        long storedCount = 0;
        foreach (var id in ids)
        {
            var properties = await reader.GetPartitionPropertiesAsync(id, deadline.Token);
            if (!properties.IsEmpty)
                storedCount += properties.LastEnqueuedSequenceNumber - properties.BeginningSequenceNumber + 1;
        }
        Assert.Equal(expected.Length, storedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_checkpoints_to_blob_and_recreated_processor_resumes_after_it(bool batched)
    {
        if (batched)
            await VerifyResume<BatchProcessContext, List<EventData>, BatchedEventProcessorClient>(
                earliest =>
                {
                    var state = new ProcessorLifecycle(earliest);
                    var processor = new ObservedBatchProcessor(CheckpointStore, ConnectionString, state);
                    return (BatchedEventHubSource.Create(new Factory<ObservedBatchProcessor>(processor)), state);
                }, context => context.Data!, hasOffsetMetadata: true);
        else
            await VerifyResume<ProcessContext, EventData, EventProcessorClient>(
                earliest =>
                {
                    var state = new ProcessorLifecycle(earliest);
                    var processor = new ObservedProcessor(CheckpointStore, ConnectionString, state);
                    return (EventHubSource.Create(new Factory<ObservedProcessor>(processor)).Where(context => context.HasEvent), state);
                }, context => [context.Data!], hasOffsetMetadata: false);
    }

    private async Task VerifyResume<TContext, TData, TProcessor>(
        Func<bool, (Source<TContext, TProcessor> Source, ProcessorLifecycle State)> create,
        Func<TContext, IEnumerable<EventData>> events, bool hasOffsetMetadata)
        where TContext : IProcessContext<TData>
        where TData : class
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var producer = new EventHubProducerClient(ConnectionString, "orders");
        var first = create(true);
        var probe = first.Source.RunWith(this.SinkProbe<TContext>(), Sys.Materializer());
        probe.EnsureSubscription();
        try
        {
            await first.State.Initialized.Task.WaitAsync(deadline.Token);
            await Send("checkpointed");
            probe.Request(1);
            var context = probe.ExpectNext(TimeSpan.FromSeconds(30));
            var checkpointed = Assert.Single(events(context));
            Assert.Equal("checkpointed", checkpointed.EventBody.ToString());
            await context.UpdateCheckpointAsync(deadline.Token);

            await AssertCheckpoint(checkpointed);
        }
        finally
        {
            probe.Cancel();
            await first.State.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        // Publish while no processor is running. A new processor starting at Latest
        // would miss these; re-reading from Earliest would replay "checkpointed".
        await Send("second", "third");
        var second = create(false);
        var resumed = second.Source.RunWith(this.SinkProbe<TContext>(), Sys.Materializer());
        resumed.EnsureSubscription();
        try
        {
            var received = new List<string>();
            while (received.Count < 2)
            {
                resumed.Request(1);
                var context = resumed.ExpectNext(TimeSpan.FromSeconds(30));
                var batch = events(context).ToArray();
                received.AddRange(batch.Select(data => data.EventBody.ToString()));
                await context.UpdateCheckpointAsync(deadline.Token);
                await AssertCheckpoint(batch.MaxBy(data => data.SequenceNumber)!);
            }
            Assert.Equal(new[] { "second", "third" }, received);
        }
        finally
        {
            resumed.Cancel();
            await second.State.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        async Task Send(params string[] payloads)
        {
            using var batch = await producer.CreateBatchAsync(new CreateBatchOptions { PartitionId = "0" }, deadline.Token);
            foreach (var payload in payloads)
                Assert.True(batch.TryAdd(new EventData(payload)));
            await producer.SendAsync(batch, deadline.Token);
        }

        async Task AssertCheckpoint(EventData data)
        {
            var checkpoints = new List<BlobItem>();
            await foreach (var blob in CheckpointStore.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }, deadline.Token))
                if (blob.Name.Contains("/checkpoint/", StringComparison.Ordinal))
                    checkpoints.Add(blob);
            var checkpoint = Assert.Single(checkpoints);
            Assert.EndsWith("/checkpoint/0", checkpoint.Name);
            // EventProcessorClient uses a sequence-number CheckpointPosition; the custom
            // batched processor also persists the legacy informational offset.
            var offset = hasOffsetMetadata ? data.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) : "no offset";
            Assert.Equal(offset, checkpoint.Metadata["offset"]);
            Assert.Equal(data.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), checkpoint.Metadata["sequencenumber"]);
        }
    }

    private sealed class Factory<T>(T processor) : IProcessorFactory<T>
        where T : EventProcessor<EventProcessorPartition>
    {
        public T CreateProcessor() => processor;
    }

    private sealed class ProcessorLifecycle(bool earliest)
    {
        private int _initialized;
        public bool Earliest { get; } = earliest;
        public TaskCompletionSource Initialized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PartitionInitialized()
        {
            if (Interlocked.Increment(ref _initialized) == 2)
                Initialized.TrySetResult();
        }
    }

    private sealed class ObservedProcessor(BlobContainerClient store, string connection, ProcessorLifecycle state)
        : EventProcessorClient(store, "orders-consumer", connection, "orders", Options())
    {
        protected override async Task OnInitializingPartitionAsync(EventProcessorPartition partition, CancellationToken token)
        {
            await base.OnInitializingPartitionAsync(partition, token);
            state.PartitionInitialized();
        }
        protected override async Task<EventProcessorCheckpoint> GetCheckpointAsync(string partitionId, CancellationToken token)
        {
            var checkpoint = await base.GetCheckpointAsync(partitionId, token);
            if (state.Earliest)
                checkpoint.StartingPosition = EventPosition.Earliest;
            return checkpoint;
        }
        public override async Task StopProcessingAsync(CancellationToken token = default)
        {
            try { await base.StopProcessingAsync(token); state.Stopped.TrySetResult(); }
            catch (Exception error) { state.Stopped.TrySetException(error); throw; }
        }
    }

    private sealed class ObservedBatchProcessor(BlobContainerClient store, string connection, ProcessorLifecycle state)
        : BatchedEventProcessorClient(store, "orders-consumer", connection, "orders", Options())
    {
        protected override async Task OnInitializingPartitionAsync(EventProcessorPartition partition, CancellationToken token)
        {
            await base.OnInitializingPartitionAsync(partition, token);
            state.PartitionInitialized();
        }
        protected override async Task<EventProcessorCheckpoint> GetCheckpointAsync(string partitionId, CancellationToken token)
        {
            var checkpoint = await base.GetCheckpointAsync(partitionId, token);
            if (state.Earliest)
                checkpoint.StartingPosition = EventPosition.Earliest;
            return checkpoint;
        }
        public override async Task StopProcessingAsync(CancellationToken token = default)
        {
            try { await base.StopProcessingAsync(token); state.Stopped.TrySetResult(); }
            catch (Exception error) { state.Stopped.TrySetException(error); throw; }
        }
    }

    private static EventProcessorClientOptions Options() => new()
    {
        LoadBalancingUpdateInterval = TimeSpan.FromSeconds(1),
        PartitionOwnershipExpirationInterval = TimeSpan.FromSeconds(5),
        MaximumWaitTime = TimeSpan.FromSeconds(1)
    };
}
