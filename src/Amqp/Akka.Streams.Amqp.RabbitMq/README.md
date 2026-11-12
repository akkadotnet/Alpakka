# Akka.Streams.Amqp.RabbitMq

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Amqp.RabbitMq.svg)](https://www.nuget.org/packages/Akka.Streams.Amqp.RabbitMq/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Amqp.RabbitMq.svg)](https://www.nuget.org/packages/Akka.Streams.Amqp.RabbitMq/)

RabbitMQ **AMQP 0-9-1** sources, publishing sinks/flows and request/reply flows for Akka.NET Streams. This package uses RabbitMQ.Client 6 and targets .NET Standard 2.0 and .NET 6. For the separate AMQP 1.0 connector, see [Akka.Streams.Amqp.V1](https://www.nuget.org/packages/Akka.Streams.Amqp.V1/).

```sh
dotnet add package Akka.Streams.Amqp.RabbitMq
```

## Publish with broker confirms

Set `RABBITMQ_URI` to a broker URI such as `amqp://guest:guest@localhost:5672/`. Both examples use the same durable `orders` queue. Existing queues must have matching declaration properties.

```csharp
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;

var system = ActorSystem.Create("rabbitmq-publisher");
try
{
    var connection = AmqpConnectionUri.Create(new Uri(
        Environment.GetEnvironmentVariable("RABBITMQ_URI")
            ?? throw new InvalidOperationException("Set RABBITMQ_URI.")));
    var sink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(connection)
        .WithRoutingKey("orders")
        .WithDeclarations(QueueDeclaration.Create("orders").WithDurable(true))
        .WithWaitForConfirms(TimeSpan.FromSeconds(5)));
    await Source.From(new[] { "first", "second", "third" })
        .Select(ByteString.FromString)
        .RunWith(sink, system.Materializer());
}
finally { await system.Terminate(); }
```

`AmqpSink.CreateSimple` accepts `ByteString`; `AmqpSink.Create` accepts `OutgoingMessage` with routing key, mandatory flag and RabbitMQ `IBasicProperties`. Both materialize a completion `Task`. Publisher confirms are opt-in through `WithWaitForConfirms(timeout)` and wait synchronously after each publish. A broker confirmation does not mean an application consumer has processed the message. Without confirms, task completion establishes that the stage issued its publishes, not confirmed broker acceptance. The `Immediate` constructor argument on `OutgoingMessage` is retained for compatibility; the connector does not send an AMQP immediate flag.

Use `AmqpFlow.Create<TPassThrough>` to publish `(OutgoingMessage, TPassThrough)` and emit each pass-through value after the corresponding publish/optional confirmation. Its materialized `Task` reports publisher completion. A durable queue alone does not make messages persistent; use persistent delivery properties on `OutgoingMessage` when needed.

## Process before acknowledging

`AmqpSource.CommittableSource(settings, bufferSize)` emits `CommittableIncomingMessage` with `Message`, `Ack(multiple: false)` and `Nack(multiple: false, requeue: true)`. The message exposes payload bytes, delivery envelope and RabbitMQ properties. Keep acknowledgment-required mode enabled when using those operations.

This complete example consumes and acknowledges three messages, then completes. Run the publisher first against the same broker/queue.

```csharp
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;

var system = ActorSystem.Create("rabbitmq-consumer");
try
{
    var connection = AmqpConnectionUri.Create(new Uri(
        Environment.GetEnvironmentVariable("RABBITMQ_URI")
            ?? throw new InvalidOperationException("Set RABBITMQ_URI.")));
    var settings = NamedQueueSourceSettings.Create(connection, "orders")
        .WithDeclarations(QueueDeclaration.Create("orders").WithDurable(true));
    await AmqpSource.CommittableSource(settings, bufferSize: 10)
        .Take(3)
        .SelectAsync(1, async delivery =>
        {
            Console.WriteLine(delivery.Message.Bytes.ToString(System.Text.Encoding.UTF8));
            await delivery.Ack();
            return Done.Instance;
        })
        .RunWith(Sink.Ignore<Done>(), system.Materializer());
}
finally { await system.Terminate(); }
```

An acknowledgment completes after the client sends the broker acknowledgment operation; it is not a distributed transaction with your application effects. Failures can cause redelivery, so make processing safe to repeat. Use `multiple: true` only when all earlier delivery tags on the same channel are ready to settle. A `Nack` with `requeue: false` rejects the delivery; dead-lettering depends on broker configuration.

`AmqpSource.AtMostOnceSource` acknowledges before emitting `IncomingMessage` downstream. A downstream failure can therefore lose that message. `TemporaryQueueSourceSettings` creates a broker-named temporary queue bound to the configured exchange/routing key. Declarations, routing and acknowledgment semantics still belong to RabbitMQ; the connector does not provide exactly-once processing.

Each materialized stage creates and owns its connection and channel. Sources use broker prefetch and a bounded stage queue; stream stages can add further buffering. A committable source keeps its connection alive after downstream cancellation until already emitted deliveries are acknowledged or rejected. Retain those delivery handles and settle them, including explicit requeue where appropriate; cancellation alone does not promise immediate connection release. Terminating the ActorSystem forcibly ends its streams and permits broker recovery of unsettled messages.

## Request/reply

`AmqpRpcFlow.CreateSimple` accepts/emits `ByteString`; `CommittableFlow` uses `OutgoingMessage` and `CommittableIncomingMessage`. The materialized `Task<string>` supplies a private exclusive reply queue name. The flow puts that name into outgoing `ReplyTo` properties. A responder can preserve those properties and publish with `AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(connection))`.

`repliesPerMessage` defaults to one. An outgoing `expectedReplies` header can override that count. Supply the expected replies or cancel/terminate the stream; there is no automatic request timeout. A responder receiving from a queue is an ongoing stream: bound it explicitly when a test or finite application expects its completion. Requests/replies are not automatically correlated by an application request identifier.

## Broker and driver compatibility

`QueueDeclaration.Create(name)` retains its existing defaults: non-durable, non-exclusive, not auto-delete. RabbitMQ 4.3+ rejects that combination by default. Prefer an explicit durable queue, as above, or a suitable exclusive temporary queue. Existing deployments using transient non-exclusive queues need the broker's documented `deprecated_features.permit.transient_nonexcl_queues = true` setting while that feature remains supported. See [RabbitMQ queue durability](https://www.rabbitmq.com/docs/queues#durability). This package does not silently change existing queue properties.

RabbitMQ.Client 7 changes public driver types and asynchronous APIs; it is not a drop-in runtime replacement for this package's version 6 API. Configure credentials, virtual host, TLS and recovery using `AmqpConnectionDetails` or `AmqpConnectionUri` as appropriate. Broker restarts, permissions and topology recovery need deployment-specific validation.

## Tests

With Docker running, from the repository root:

```sh
dotnet test src/Amqp/Akka.Streams.Amqp.RabbitMq.Tests/Akka.Streams.Amqp.RabbitMq.Tests.csproj -c Release --framework net10.0
```

The Testcontainers.RabbitMq fixture pins RabbitMQ 4.4.0, discovers its endpoint, bounds startup and owns cleanup. Its compatibility setting keeps the original default-queue contracts exercised. A separate stock-broker case observes rejection of legacy defaults, durable-queue native-client interoperability and explicit requeue after cancellation. A blocked-broker case keeps a confirmed publish pending until the broker accepts it. The suite includes routing, acknowledgments, rejection and the three formerly skipped RPC cases. Missing Docker or broker startup failures fail the tests. These local single-broker checks do not establish cluster recovery or production throughput.
