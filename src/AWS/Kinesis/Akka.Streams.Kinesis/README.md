# Akka.Streams.Kinesis

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Kinesis.svg)](https://www.nuget.org/packages/Akka.Streams.Kinesis/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Kinesis.svg)](https://www.nuget.org/packages/Akka.Streams.Kinesis/)

Amazon **Kinesis Data Streams** publishing and single-shard polling for Akka.NET Streams. The package uses AWSSDK.Kinesis 4.x and targets .NET Standard 2.0 and .NET 6. The `Firehose` namespace contains public empty placeholders; it does not implement a Firehose connector.

```sh
dotnet add package Akka.Streams.Kinesis
```

Provision a stream separately. Set `KINESIS_STREAM_NAME`; the examples use the SDK's credential discovery and `AWS_REGION` (default `us-east-1`). Grant the appropriate stream permissions. Each materialization calls the supplied client factory and **owns/disposes its client**, including source completion, failure and cancellation. Return a new client each time; do not return a shared client you intend to reuse elsewhere.

## Publish and retain sequence numbers

`KinesisFlow.Create` accepts SDK `PutRecordsRequestEntry` values and emits successful `PutRecordsResultEntry` values. `ByPartitionAndBytes` accepts `(string partitionKey, ByteString data)`; `ByPartitionAndData` accepts `(string partitionKey, MemoryStream data)`. Each flow materializes `NotUsed`; combine it with a downstream sink to observe completion.

```csharp
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Kinesis;
using Amazon;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;

var name = Environment.GetEnvironmentVariable("KINESIS_STREAM_NAME")
    ?? throw new InvalidOperationException("Set KINESIS_STREAM_NAME.");
var region = RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1");
var system = ActorSystem.Create("kinesis-publisher");
try
{
    var results = await Source.From(new[] { "first", "second", "third" })
        .Select(payload => ("application-key", ByteString.FromString(payload)))
        .Via(KinesisFlow.ByPartitionAndBytes(name, client: () => new AmazonKinesisClient(region)))
        .RunWith(Sink.Seq<PutRecordsResultEntry>(), system.Materializer());
    foreach (var result in results)
        Console.WriteLine($"{result.ShardId}: {result.SequenceNumber}");
}
finally { await system.Terminate(); }
```

`KinesisSink.Create` and its tuple variants discard publication responses and materialize **NotUsed**, not a completion task. Use a flow followed by `Sink.Ignore<PutRecordsResultEntry>()` when completion/failure needs to be awaited.

The default flow settings use parallelism two, maximum batch size 500, five retries, a 500 ms initial exponential backoff, and shaping budgets of 1,000 records/second and 1,000,000 estimated bytes/second. Configure a settings instance explicitly for your workload. The existing setter is named `WithPRetryInitialTimeout`.

The byte estimate currently uses `PartitionKey.Length + Data.Position`; it does not measure UTF-8 key bytes plus stream length. Raw streams positioned at zero can therefore undercount payload bytes. These budgets are not a guarantee that every shard meets current AWS limits. Keep requests within service limits and validate real shard distribution/throughput for your deployment.

Partial `PutRecords` successes are emitted and failed entries are retried independently. Concurrent retry jobs are retained separately. Retries can reorder results relative to original input and can repeat external effects; there is no exactly-once delivery guarantee. Exponential backoff doubles the initial delay by attempt; the `Linear` strategy uses initial delay multiplied by attempt count. Exhausting retries fails with `PublishingRecordsException`, whose `Attempt` and `Errors` expose the attempted requests/results. Whole-request SDK failures fail the stream rather than using this per-record retry path; SDK client retries are a separate policy.

## Read a shard

`KinesisSource.Basic` emits SDK `Record` values from one configured shard and materializes `NotUsed`. Discover shard IDs with the SDK; merge multiple sources explicitly if needed. This example reads three records from the beginning of a known shard. Set `KINESIS_SHARD_ID` as well as the stream name.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Kinesis;
using Amazon;
using Amazon.Kinesis;

var name = Environment.GetEnvironmentVariable("KINESIS_STREAM_NAME")
    ?? throw new InvalidOperationException("Set KINESIS_STREAM_NAME.");
var shard = Environment.GetEnvironmentVariable("KINESIS_SHARD_ID")
    ?? throw new InvalidOperationException("Set KINESIS_SHARD_ID.");
var region = RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1");
var system = ActorSystem.Create("kinesis-reader");
try
{
    var settings = ShardSettings.Create(name, shard).WithShardIteratorType(ShardIteratorType.TRIM_HORIZON);
    var records = await KinesisSource.Basic(settings, () => new AmazonKinesisClient(region))
        .Take(3).RunWith(Sink.Seq<Amazon.Kinesis.Model.Record>(), system.Materializer());
    foreach (var record in records)
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(record.Data.ToArray()));
}
finally { await system.Terminate(); }
```

Default shard settings start at `LATEST`, poll empty responses after one second and request up to 500 records. `WithLimit` accepts 1–10,000. A source can fetch/buffer before downstream requests and fetch the next batch after draining its buffer. Backpressure controls emission, not a promise of zero speculative SDK work. A terminal response with no next iterator drains all its records before completion; an empty terminal response completes immediately.

Checkpointing, shard discovery/resharding and automatic recovery from expired iterators are not implemented. Save sequence numbers after successful application processing. To restart at/after a saved sequence, set `WithStartingSequenceNumber(sequence)` **before** `WithShardIteratorType(AT_SEQUENCE_NUMBER/AFTER_SEQUENCE_NUMBER)`; timestamp iterators similarly need `WithAtTimestamp` before their iterator type. SDK errors fail the source. Calls already issued do not receive a stream cancellation token; shutdown disposes the owned SDK client, so outstanding requests may be interrupted.

## Tests

With Docker running, from the repository root:

```sh
dotnet test src/AWS/Kinesis/Akka.Streams.Kinesis.Tests/Akka.Streams.Kinesis.Tests.csproj -c Release --framework net10.0
```

The retained unit tests and lifecycle regressions cover demand, terminal-batch draining, finite publication, concurrent failed-batch retries and owned-client disposal. Integration uses Testcontainers.Floci with pinned `floci/floci:1.5.13`, discovered endpoints, bounded readiness and owned cleanup. Native SDK peers verify all three publishing modes, exact UTF-8 data/sequence numbers, trim/at/after source positions, sink completion and missing-stream failure.

Floci represents a missing-stream `PutRecords` as per-entry `InternalFailure`, unlike AWS's request-level error; a native positive control characterizes that shape before asserting the connector's retry-exhaustion result. Tests use one shard and do not claim AWS MD5 partition placement: Floci's shard-selection algorithm differs. They do not prove cloud IAM/KMS, real throughput, resharding, Firehose behavior or production durability. Missing Docker or emulator startup failures fail the suite.
