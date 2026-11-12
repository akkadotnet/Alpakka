# Akka.Streams.Azure.StorageQueue

[![NuGet](https://img.shields.io/nuget/v/Akka.Streams.Azure.StorageQueue.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.StorageQueue)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Azure.StorageQueue.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.StorageQueue)

Azure Queue Storage sources and sinks for [Akka.NET Streams](https://getakka.net/articles/streams/introduction.html), using an `Azure.Storage.Queues.QueueClient` supplied by your application. This package targets .NET Standard 2.0 and .NET 6.0.

```shell
dotnet add package Akka.Streams.Azure.StorageQueue
```

The connector sends strings and receives SDK `QueueMessage` values. It does not serialize application objects, create queues, or delete received messages automatically. Configure credentials, endpoints and message encoding on your `QueueClient`.

## Send messages

Set `AZURE_STORAGE_CONNECTION_STRING` to your account connection string. This complete example explicitly creates an `orders` queue, sends two messages, and waits for the final send:

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.StorageQueue;
using Akka.Streams.Dsl;
using Azure.Storage.Queues;

var queue = new QueueClient(
    Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set AZURE_STORAGE_CONNECTION_STRING."), "orders");
await queue.CreateIfNotExistsAsync();
var system = ActorSystem.Create("queue-publisher");
try
{
    await Source.From(new[] { "first", "second" })
        .RunWith(QueueSink.Create(queue), system.Materializer());
}
finally
{
    await system.Terminate();
}
```

`QueueSink.Create` consumes strings, sends one message at a time, and materializes a `Task`. `SourceExtension.ToStorageQueue` runs the same sink directly. The task completes after upstream finishes and the final send succeeds. `AddRequestOptions` forwards an optional time-to-live and initial visibility delay to `QueueClient.SendMessageAsync`; both default to `null`, leaving their defaults to the SDK/service.

## Receive and delete after processing

Run this example separately against the same queue. It processes two messages and explicitly deletes each one after processing:

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.StorageQueue;
using Akka.Streams.Dsl;
using Azure.Storage.Queues;

var queue = new QueueClient(
    Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set AZURE_STORAGE_CONNECTION_STRING."), "orders");
var system = ActorSystem.Create("queue-subscriber");
try
{
    await QueueSource.Create(queue, prefetchCount: 1,
            options: new GetRequestOptions(visibilityTimeout: TimeSpan.FromMinutes(1)))
        .Take(2)
        .SelectAsync(1, async message =>
        {
            Console.WriteLine(message.MessageText); // Replace with your processing code.
            await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt);
            return message.MessageId;
        })
        .RunWith(Sink.Ignore<string>(), system.Materializer());
}
finally
{
    await system.Terminate();
}
```

`QueueSource.Create` materializes `NotUsed`. Its public `QueueSource` stage constructor accepts the same settings. Defaults are `prefetchCount: 10`, a ten-second polling interval after an empty receive, and a `GetRequestOptions.VisibilityTimeout` of `null`, which delegates visibility duration to the SDK/service.

Receiving makes a message temporarily invisible; it does not acknowledge successful processing. Delete with the current message ID and pop receipt after processing succeeds. Without deletion, messages can be delivered again when visibility expires. Long processing may require `QueueClient.UpdateMessageAsync`; use its new pop receipt for subsequent updates or deletion. Choose visibility duration and receive batch size for the time messages spend waiting in your stream. SDK/service message-size and lifetime limits still apply.

Default stopping supervision propagates failures. For the sink's existing send-failure handling, `Resume` retries the same message and `Restart` skips it. Retries can duplicate sends, and skipping can lose a message. For receive failures, a non-stopping source strategy schedules another poll. The connector does not provide exactly-once delivery or poison-message routing.

## Lifetime and local tests

Your application owns the `QueueClient`, queue and `ActorSystem`. Stream completion or cancellation does not delete the queue. Stop streams before shutting down their system or backing service. An existing SDK request may remain in flight after stream cancellation.

From the repository root, with the pinned .NET SDK and Docker running Linux containers:

```shell
dotnet test src/Azure/Akka.Streams.Azure.StorageQueue.Tests/Akka.Streams.Azure.StorageQueue.Tests.csproj -c Release --framework net10.0
```

The tests use `Testcontainers.Azurite` to start Azurite 3.35.0 for each case, discover its connection string, and remove the owned container afterward. They do not require a separately installed Azurite, fixed host ports or an Azure account. The Windows/.NET Framework test target can also run when a Linux Docker endpoint is available.

Azurite tests establish the exercised queue operations against that pinned emulator. They do not establish Azure identity configuration, service-scale behavior or managed-service durability. See [Azure Queue Storage client documentation](https://learn.microsoft.com/dotnet/api/overview/azure/storage.queues-readme) and [Azurite's documented differences](https://learn.microsoft.com/azure/storage/common/storage-use-azurite#differences-between-azurite-and-azure-storage).

## License

Apache-2.0
