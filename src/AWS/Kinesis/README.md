# Akka.NET Streams connectors for AWS services

## Amazon Kinesis streams

`Akka.Streams.Kinesis` can be used to either put data directly to [AWS Kinesis](https://aws.amazon.com/kinesis/) streams (using `KinesisFlow` or `KinesisSink`) or reading from it (using `KinesisSource`).

Example of sending the data:

```csharp
using Akka.Streams.Kinesis;

const string streamName = "test-stream";
const string partitionKey = "partition-key";

/// Any client passed to KinesisFlow will then be managed by that flow, and disposed on graph stop.
Func<IAmazonKinesis> clientFactory = () =>
    new AmazonKinesisClient(new BasicAWSCredentials(accessKey, secretKey), RegionEndpoint.USWest1);

Source.From(new [] { "hello", "world" })
    .Select(data => new PutRecordsRequestEntry {
        PartitionKey = partitionKey,
        Data = new MemoryStream(Encoding.UTF8.GetBytes(data))
    })
    .Via(KinesisFlow.Create(streamName, KinesisFlowSettings.Default, clientFactory))
    .RunForeach(response => Console.WriteLine(response.SequenceNumber), materializer);
```

Example of receiving data:

```csharp
using Akka.Streams.Kinesis;

const string streamName = "test-stream";
const string shardId = "shard-id";
string startFrom = null;

/// Any client passed to KinesisSource will then be managed by that source, and disposed on graph stop.
Func<IAmazonKinesis> clientFactory = () =>
    new AmazonKinesisClient(new BasicAWSCredentials(accessKey, secretKey), RegionEndpoint.USWest1);

var settings = ShardSettings.Create(streamName, shardId)
    .WithStartingSequenceNumber(startFrom);

KinesisSource.Basic(settings, clientFactory)
    .Select(x => Encoding.UTF8.GetString(x.Data.ToArray()))
    .RunForeach(Console.WriteLine, materializer);
```