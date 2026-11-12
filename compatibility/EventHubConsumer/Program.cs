using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Azure.EventHub;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;

internal static class Program
{
    private const string Connection = "Endpoint=sb://unit.test/;SharedAccessKeyName=test;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static async Task Main()
    {
        // Construct public graphs and context/checkpoint delegates without contacting Azure.
        var source = EventHubSource.Create(new Factory());
        var batched = BatchedEventHubSource.Create(new BatchFactory());
        await using var producer = new EventHubProducerClient(Connection, "test-hub");
        var sink = EventHubSink.Create(producer);
        var sinkStage = new EventHubSink(producer);
        if (source is null || batched is null || sink is null || sinkStage.In is null || producer.IsClosed)
            throw new InvalidOperationException("A retained factory API changed behavior.");

        var partition = new TestPartitionContext();
        var data = new EventData("old-consumer-payload");
        var checkpoints = 0;
        Task Checkpoint(CancellationToken token) { token.ThrowIfCancellationRequested(); checkpoints++; return Task.CompletedTask; }
        var context = new ProcessContext(new ProcessEventArgs(partition, data, Checkpoint, CancellationToken.None));
        var batch = new BatchProcessContext(new ProcessEventBatchArgs(partition, new[] { data }, Checkpoint));
        IProcessContext<EventData> singleContract = context;
        IProcessContext<List<EventData>> batchContract = batch;
        await singleContract.UpdateCheckpointAsync();
        await batchContract.UpdateCheckpointAsync();
        if (!context.HasEvent || !batch.HasEvents || context.Data?.EventBody.ToString() != "old-consumer-payload"
            || batch.Data?.Count != 1 || checkpoints != 2 || context.Partition.PartitionId != "0")
            throw new InvalidOperationException("Retained context/checkpoint APIs changed behavior.");
        Console.WriteLine("Previously compiled Event Hubs consumer passed (factory and context/checkpoint APIs).");
    }

    private sealed class Factory : IProcessorFactory<EventProcessorClient>
    {
        public EventProcessorClient CreateProcessor() => new EventProcessorClient(
            new BlobContainerClient("UseDevelopmentStorage=true", "checkpoints"), "test-group", Connection, "test-hub");
    }
    private sealed class TestPartitionContext : PartitionContext
    {
        public TestPartitionContext() : base("unit.test", "test-hub", "test-group", "0") { }
    }
    private sealed class BatchFactory : IProcessorFactory<BatchedEventProcessorClient>
    {
        public BatchedEventProcessorClient CreateProcessor() => new BatchedEventProcessorClient(
            new BlobContainerClient("UseDevelopmentStorage=true", "checkpoints"), "test-group", Connection, "test-hub");
    }
}
