# Akka.Streams.Azure.ServiceBus

[![NuGet](https://img.shields.io/nuget/v/Akka.Streams.Azure.ServiceBus.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.ServiceBus)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Azure.ServiceBus)](https://www.nuget.org/packages/Akka.Streams.Azure.ServiceBus)

Azure Service Bus sources and batched sinks for [Akka.NET Streams](https://getakka.net/articles/streams/introduction.html), using `Azure.Messaging.ServiceBus` clients supplied by your application. The same APIs work with queues and with topic subscriptions.

```shell
dotnet add package Akka.Streams.Azure.ServiceBus
```

The package targets .NET Standard 2.0 and .NET 6.0. Your application needs an Akka.NET `ActorSystem`, an existing Service Bus entity, and permission to send or receive from it. This package does not provision Azure resources or manage client credentials.

## Publish batches

Set `SERVICEBUS_CONNECTION_STRING` to your namespace connection string. This example sends two messages to an existing `orders` queue or topic and waits for the last batch to finish:

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(
    Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set SERVICEBUS_CONNECTION_STRING."));
await using var sender = client.CreateSender("orders");
var system = ActorSystem.Create("service-bus-publisher");
try
{
    await Source.From(new[] { "first", "second" })
        .Select(value => new ServiceBusMessage(value))
        .Grouped(10)
        .RunWith(ServiceBusSink.Create(sender), system.Materializer());
}
finally
{
    await system.Terminate();
}
```

`ServiceBusSink.Create` consumes `IEnumerable<ServiceBusMessage>` batches and materializes a `Task`. It sends one batch at a time; the task completes after upstream finishes and the final send succeeds. `SourceExtension.ToServiceBus` is a shortcut for running the same sink. Choose batches that fit your service's message and batch-size limits; this connector does not split oversized batches automatically.

## Receive and settle messages

For a queue, use `client.CreateReceiver("orders")`. For a topic, use `client.CreateReceiver("orders", "shipping-subscriber")`. With the default `PeekLock` receive mode, the raw-message source leaves settlement to your application:

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(
    Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set SERVICEBUS_CONNECTION_STRING."));
await using var receiver = client.CreateReceiver("orders", "shipping-subscriber");
var system = ActorSystem.Create("service-bus-subscriber");
try
{
    await ServiceBusSource.Create(receiver)
        .Take(10)
        .SelectAsync(1, async message =>
        {
            Console.WriteLine(message.Body); // Replace with your processing code.
            await receiver.CompleteMessageAsync(message);
            return message.MessageId;
        })
        .RunWith(Sink.Ignore<string>(), system.Materializer());
}
finally
{
    await system.Terminate();
}
```

Complete a message only after processing succeeds. Abandon, defer, dead-letter, and lock renewal are available through your receiver. With `ReceiveAndDelete`, Service Bus removes messages as they are received; application completion is not appropriate for that mode.

There are three source forms:

| API | Deserialization and settlement |
| --- | --- |
| `ServiceBusSource.Create(receiver)` | Emits `ServiceBusReceivedMessage`; the application handles settlement. |
| `ServiceBusSource.Create(receiver, extractor)` | Applies your synchronous extractor; the application still handles settlement. |
| `ServiceBusSource.Create<T>(receiver)` | Deserializes the body as JSON and awaits automatic completion before emitting it. Completion failures fail the stream. Use manual settlement when processing must finish before acknowledgment. |

All source forms materialize `NotUsed`. Defaults are a maximum receive batch of 100 messages, a server wait of three seconds, and a ten-second polling interval after an empty receive. The source receives when downstream requests data; a receive batch may contain more messages than current downstream demand. Keep that in mind when choosing lock duration and batch size.

The default stopping supervision strategy propagates failures. For the sink's existing send-failure handling, `Resume` retries the failed batch and `Restart` moves on to the next batch. Retrying a batch can produce duplicates; skipping a batch can lose messages. This connector does not provide exactly-once processing or transaction management.

## Ownership and shutdown

Your application owns the `ServiceBusClient`, sender, receiver, and `ActorSystem`. Stream completion or cancellation does not dispose your clients. Stop streams before disposing those resources. Cancellation can leave an existing SDK receive or settlement operation in flight; settle or release remaining locks using your receiver and normal Service Bus policies.

## Local sample and integration tests

From the repository root, with the pinned .NET SDK, Aspire CLI, and Docker running Linux containers:

```shell
aspire start --apphost src/Azure/Examples/Simple/ServiceBus/ServiceBus.AppHost/ServiceBus.AppHost.csproj
dotnet test src/Azure/Akka.Streams.Azure.ServiceBus.Tests/Akka.Streams.Azure.ServiceBus.Tests.csproj -c Release
```

The sample starts Microsoft's Service Bus emulator and its SQL Server companion, declares the `orders` topic and `shipping-subscriber` subscription, and runs a producer and consumer. The tests use `Aspire.Hosting.Testing` to start an isolated copy with sample traffic disabled and a test queue. They obtain the endpoint from Aspire; no Azure account or fixed local broker port is required. The emulator and SQL Server images have their own license terms.

Local emulator results do not establish Azure identity configuration, service quotas, availability, or durability across emulator restarts. See the [Service Bus emulator limitations](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) and [connector documentation](https://alpakka.getakka.net/documentation/Azure/ServiceBus.html).

## License

Apache-2.0
