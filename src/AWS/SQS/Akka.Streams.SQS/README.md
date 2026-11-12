# Akka.Streams.SQS

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.SQS.svg)](https://www.nuget.org/packages/Akka.Streams.SQS/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.SQS.svg)](https://www.nuget.org/packages/Akka.Streams.SQS/)

Amazon SQS publishing, receiving and acknowledgment stages for Akka.NET Streams. This package uses **AWSSDK.SQS 3.x**, exposes its public request/response types, and targets .NET Standard 2.0 and .NET 6. SDK 4 is not a drop-in replacement for those public driver contracts.

```sh
dotnet add package Akka.Streams.SQS
```

Create the queue separately and set `SQS_QUEUE_URL`. The examples use the AWS SDK's normal credential discovery and `AWS_REGION` (default `us-east-1`); configure credentials or a role with the required queue permissions. For a local emulator, configure an `AmazonSQSClient` with its discovered `AmazonSQSConfig.ServiceURL`, `AuthenticationRegion` and test credentials instead.

## Publish messages

`SqsPublishSink.Default` accepts strings and returns a `Task<Done>` that completes after the sends finish. Its supplied `IAmazonSQS` is borrowed; keep it alive for the stream and dispose it afterwards.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.SQS;
using Amazon;
using Amazon.SQS;

using var client = new AmazonSQSClient(RegionEndpoint.GetBySystemName(
    Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"));
var queueUrl = Environment.GetEnvironmentVariable("SQS_QUEUE_URL")
    ?? throw new InvalidOperationException("Set SQS_QUEUE_URL.");
var system = ActorSystem.Create("sqs-publisher");
try
{
    await Source.From(new[] { "first", "second", "third" })
        .RunWith(SqsPublishSink.Default(client, queueUrl), system.Materializer());
}
finally { await system.Terminate(); }
```

The publishing APIs offer three modes:

| API | Input | Result |
| --- | --- | --- |
| `SqsPublishFlow.Default` | `SendMessageRequest` | `SqsPublishResult` with the original request and SDK response |
| `SqsPublishFlow.Grouped` | `SendMessageRequest` | Individual `SqsPublishResultEntry` values after grouping |
| `SqsPublishFlow.Batch` | `IEnumerable<SendMessageRequest>` | A list of `SqsPublishResultEntry` values for each input batch |

`SqsPublishSink.Grouped`/`Batch` accept strings and discard those detailed results. `MessageSink`, `GroupedMessageSink` and `BatchedMessageSink` accept SDK requests. For single-request `Default`/`MessageSink`, set each request's `QueueUrl`: the `queueUrl` argument does not rewrite that request. String sinks create requests with the supplied URL. Grouped/batch publishing uses the supplied batch queue URL.

Batch results are associated with their original requests by SQS batch entry ID, even when the SDK returns successes in a different order. A partial batch failure fails the stream with `SqsBatchException`; successful entries may already have been sent. The connector does not retry just the failed entries or undo successful sends. AWS client retries or application retries can duplicate delivery.

The batch path forwards the body, message attributes, message-group ID and deduplication ID. Per-message `DelaySeconds` and system attributes are not copied into batch entries by this version; use a single-request API or suitable queue settings when those fields matter. Keep batches within SQS limits and do not infer FIFO ordering/deduplication guarantees from standard-queue examples.

## Receive, process and delete

`SqsSource.Create` emits AWS SDK `Message` objects and materializes `NotUsed`. Receiving makes messages invisible temporarily; it **does not delete or automatically acknowledge them**. After successful application processing, pass `MessageAction.Delete(message)` through an acknowledgment flow or sink.

This complete example processes three messages from the publisher and then completes. It limits batch/buffer size to one; the source can still issue a receive ahead of downstream completion.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.SQS;
using Amazon;
using Amazon.SQS;

using var client = new AmazonSQSClient(RegionEndpoint.GetBySystemName(
    Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"));
var queueUrl = Environment.GetEnvironmentVariable("SQS_QUEUE_URL")
    ?? throw new InvalidOperationException("Set SQS_QUEUE_URL.");
var system = ActorSystem.Create("sqs-consumer");
try
{
    var settings = SqsSourceSettings.Default.WithWaitTime(TimeSpan.FromSeconds(1))
        .WithMaxBatchSize(1).WithMaxBufferSize(1);
    await SqsSource.Create(client, queueUrl, settings)
        .Take(3)
        .Select(message =>
        {
            Console.WriteLine(message.Body);
            return MessageAction.Delete(message);
        })
        .RunWith(SqsAckSink.Default(client, queueUrl), system.Materializer());
}
finally { await system.Terminate(); }
```

`MessageAction.Ignore` leaves the message untouched; it can be received again after its visibility timeout. `ChangeVisibility(message, timeout)` changes that timeout; zero makes it available again immediately. Use the current receipt handle from the most recent receive. `SqsAckFlow.Default` emits individual acknowledgment results; `Grouped` batches deletes/visibility changes and merges the action branches, so output order can differ from input order. Grouped partial failures throw `SqsBatchException` after some operations may already have succeeded.

Default source settings use a 20-second long poll, batch size 10, buffer size 100 and parallelism one. Configure system/message attributes explicitly with `WithAttributes`/`WithMessageAttributes`; use `WithVisibilityTimeout` to override the queue's receive visibility. `WithCloseOnEmptyReceive(true)` completes on an empty response; its default is false and it keeps polling. An empty response is not proof that no message can arrive later.

Stream backpressure bounds emitted data, but SDK receives, the source buffer and downstream buffers allow prefetch/in-flight work. `Take` or cancellation can leave received, undeleted messages invisible until visibility expires. Cancellation does not cancel SDK calls already issued by these stages. Configure bounded client timeouts/polls and manage the borrowed client's lifetime. A client exception normally fails the stream; application supervision/retries need to account for partially completed external effects. Neither a completed stream nor an SQS delete provides an exactly-once transaction with your application work.

## Tests

With Docker running, from the repository root:

```sh
dotnet test src/AWS/SQS/Akka.Streams.SQS.Tests/Akka.Streams.SQS.Tests.csproj -c Release --framework net10.0
```

The existing unit tests are retained. Integration tests use Testcontainers.Floci with pinned `floci/floci:1.5.13`, discovered endpoints, bounded startup and owned cleanup. They compare single/grouped/batch publishing results with native SDK message IDs, and exercise attributes, visibility changes, ignore/delete actions, close-on-empty and missing-queue failures. Held-message controls check both visible and invisible emulator counts so an empty receive alone cannot stand in for deletion. Missing Docker or emulator setup failures fail the suite. Standard-queue emulator tests do not establish AWS IAM/KMS, cloud consistency/scaling, FIFO behavior or production durability.
