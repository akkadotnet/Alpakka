# Akka.Streams.Sns

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Sns.svg)](https://www.nuget.org/packages/Akka.Streams.Sns/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Sns.svg)](https://www.nuget.org/packages/Akka.Streams.Sns/)

Publish strings to Amazon SNS topics through Akka.NET Streams. This package uses **AWSSDK.SimpleNotificationService 4.x**, exposes its `IAmazonSimpleNotificationService` and `PublishResponse` types, and targets .NET Standard 2.0 and .NET 6.

```sh
dotnet add package Akka.Streams.Sns
```

Create a standard SNS topic and configure its subscribers separately. Set `SNS_TOPIC_ARN`; the examples use the AWS SDK's normal credential discovery and `AWS_REGION` (default `us-east-1`). Supply credentials or a role with permission to publish. An accepted publish does not establish that every subscriber received or processed the message.

## Publish and await completion

`SnsPublisher.PlainSink(topicArn, client)` accepts strings and materializes a `Task<Done>`. The task completes after upstream finishes and all its publishes have completed. The supplied SDK client is **borrowed**: the connector does not dispose it. Keep it alive while the stream and any already issued SDK calls need it.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Sns;
using Amazon;
using Amazon.SimpleNotificationService;

using var client = new AmazonSimpleNotificationServiceClient(RegionEndpoint.GetBySystemName(
    Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"));
var topic = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN")
    ?? throw new InvalidOperationException("Set SNS_TOPIC_ARN.");
var system = ActorSystem.Create("sns-publisher");
try
{
    await Source.From(new[] { "first", "second", "third" })
        .RunWith(SnsPublisher.PlainSink(topic, client), system.Materializer());
}
finally { await system.Terminate(); }
```

## Keep each SDK response

`SnsPublisher.PlainFlow(topicArn, client)` accepts strings, emits the corresponding SDK `PublishResponse`, and materializes `NotUsed`. Responses are emitted in input order, with one publish in flight at a time. The final response is followed by completion without requiring another downstream request.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Sns;
using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

using var client = new AmazonSimpleNotificationServiceClient(RegionEndpoint.GetBySystemName(
    Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"));
var topic = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN")
    ?? throw new InvalidOperationException("Set SNS_TOPIC_ARN.");
var system = ActorSystem.Create("sns-responses");
try
{
    var responses = await Source.From(new[] { "first", "second", "third" })
        .Via(SnsPublisher.PlainFlow(topic, client))
        .RunWith(Sink.Seq<PublishResponse>(), system.Materializer());
    foreach (var response in responses)
        Console.WriteLine(response.MessageId);
}
finally { await system.Terminate(); }
```

The connector sends each input as the `Message` of an SNS `PublishRequest` with the supplied topic ARN. It does not expose subject, message attributes, message structure, FIFO group/deduplication IDs or batching options. Use the SDK directly when those request features are required. Subscription protocol, raw-message delivery and subscriber permissions are configured on SNS/SQS, not by these stages. An SQS subscriber normally receives an SNS JSON envelope; a raw-message subscription receives the payload directly.

Stream backpressure controls publication, while upstream/downstream stages may still buffer data. Standard SNS delivery and SDK/application retries can produce duplicates; this connector does not add exactly-once processing, persistence or a subscriber acknowledgment protocol. Retrying after a failure can repeat a publish whose outcome was uncertain.

SDK failures fail the stream. A faulted SDK task retains its `AggregateException` wrapper; inspect `GetBaseException()` for the service error. A canceled SDK publish fails with `TaskCanceledException`, rather than reporting successful stream completion. Upstream failures also fail the stage. Stream cancellation does not cancel a publish already issued to the SDK; configure bounded SDK timeouts and manage the borrowed client's lifetime. This stage does not implement a retry or supervision policy of its own.

## Tests

With Docker running, from the repository root:

```sh
dotnet test src/AWS/SNS/Akka.Streams.Sns.Tests/Akka.Streams.Sns.Tests.csproj -c Release --framework net10.0
dotnet test src/AWS/SNS/Akka.Streams.Sns.IntegrationTests/Akka.Streams.Sns.IntegrationTests.csproj -c Release
```

The existing test project retains its net10.0/net48 targets and tests the publication, demand, failure and canceled-task contracts. The separate .NET 10 integration project uses Testcontainers.Floci and pinned `floci/floci:1.5.13`, discovered endpoints, owned resources and bounded startup/cleanup. Native SQS subscriptions verify exact wrapped/raw payloads, SNS response IDs for wrapped delivery, sink completion, SDK errors and borrowed-client usability. Setup failures fail the suite.

The observer process uses SQS/Core SDK 4.1 with the SNS 4.0 client. The shipped library and original contracts retain their SNS/Core 4.0 minimum graph; the separate SQS connector keeps its SDK 3 dependency. These standard-topic emulator tests do not establish AWS IAM/KMS enforcement, FIFO behavior, production consistency, subscriber processing or durability across failures.
