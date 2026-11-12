using System;
using System.IO;
using Akka;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Kinesis;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Runtime;

internal static class Program
{
    private static void Main()
    {
        var defaults = KinesisFlowSettings.Default;
        if (defaults.Parallelism != 2 || defaults.MaxBatchSize != 500 || defaults.MaxRetries != 5
            || defaults.RetryInitialTimeout != TimeSpan.FromMilliseconds(500))
            throw new InvalidOperationException("Retained flow defaults changed.");
        var shard = ShardSettings.Create("stream", "shard");
        if (shard.Limit != 500 || shard.RefreshInterval != TimeSpan.FromSeconds(1) || shard.ShardIteratorType != ShardIteratorType.LATEST)
            throw new InvalidOperationException("Retained shard defaults changed.");
        var configured = shard.WithStartingSequenceNumber("123").WithShardIteratorType(ShardIteratorType.AFTER_SEQUENCE_NUMBER);
        Func<IAmazonKinesis> factory = () => new AmazonKinesisClient(new BasicAWSCredentials("test", "test"),
            new AmazonKinesisConfig { ServiceURL = "http://unit.test", AuthenticationRegion = "us-east-1" });
        Flow<PutRecordsRequestEntry, PutRecordsResultEntry, NotUsed> flow = KinesisFlow.Create("stream", client: factory);
        Flow<(string, ByteString), PutRecordsResultEntry, NotUsed> bytes = KinesisFlow.ByPartitionAndBytes("stream", client: factory);
        Flow<(string, MemoryStream), PutRecordsResultEntry, NotUsed> data = KinesisFlow.ByPartitionAndData("stream", client: factory);
        Sink<PutRecordsRequestEntry, NotUsed> sink = KinesisSink.Create("stream", client: factory);
        Source<Amazon.Kinesis.Model.Record, NotUsed> source = KinesisSource.Basic(configured, factory);
        var error = new PublishingRecordsException(2, Array.Empty<(PutRecordsRequestEntry, PutRecordsResultEntry)>());
        if (flow is null || bytes is null || data is null || sink is null || source is null || error.Attempt != 2
            || configured.StartingSequenceNumber != "123" || shard.StartingSequenceNumber != null)
            throw new InvalidOperationException("Retained factories, settings or error API changed.");
        Console.WriteLine("Previously compiled Kinesis consumer passed (factories, settings and exception APIs).");
    }
}
