# Akka.Streams.Amqp.V1

[![NuGet](https://img.shields.io/nuget/v/Akka.Streams.Amqp.V1.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Amqp.V1)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Amqp.V1.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Amqp.V1)

AMQP 1.0 sources and sinks for [Akka.NET Streams](https://getakka.net/articles/streams/introduction.html), using AMQPNetLite and application-selected Akka.NET serializers. The package targets .NET Standard 2.0 and .NET 6.0. For RabbitMQ's AMQP 0-9-1 adapter, see [Akka.Streams.Amqp.RabbitMq](https://www.nuget.org/packages/Akka.Streams.Amqp.RabbitMq).

```shell
dotnet add package Akka.Streams.Amqp.V1
```

Use a broker with an AMQP 1.0 acceptor. Queue/address creation, authorization and routing depend on the broker configuration. This connector supplies AMQP link settings; it does not configure the broker.

## Publish and receive

Set `AMQP_CONNECTION_STRING` to an AMQP address including your broker's endpoint and credentials. This complete example publishes two strings and receives two from a dedicated `alpakka-demo` queue. Provision that queue or enable the broker's corresponding automatic-creation policy first.

```csharp
using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Amqp.V1;
using Akka.Streams.Amqp.V1.Dsl;
using Akka.Streams.Dsl;
using Amqp;
using Address = Amqp.Address;

var address = new Address(Environment.GetEnvironmentVariable("AMQP_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set AMQP_CONNECTION_STRING."));
var system = ActorSystem.Create("amqp-example");
var serializer = system.Serialization.FindSerializerForType(typeof(string));
var sender = new AddressSinkSettings<string>(address, "demo-sender", "alpakka-demo", serializer);
var receiver = new AddressSourceSettings<string>(address, "demo-receiver", "alpakka-demo", 10, serializer);
try
{
    await Source.From(new[] { "first", "second" })
        .RunWith(AmqpSink.Create(sender), system.Materializer());
    var messages = await AmqpSource.Create(receiver).Take(2)
        .RunWith(Sink.Seq<string>(), system.Materializer());
    foreach (var message in messages)
        Console.WriteLine(message);
}
finally
{
    await system.Terminate();
    await receiver.CloseConnectionAsync();
    await sender.CloseConnectionAsync();
}
```

`AmqpSink.Create<T>` consumes values and materializes a `Task` reporting upstream completion/failure, synchronous conversion/send failures and link disconnection. A disconnection reported by the link callback uses `DisconnectedException`; its `Error` can be `null` when no AMQP error was supplied. `AmqpSource.Create<T>` emits values and materializes `NotUsed`.

The built-in settings serialize values with the supplied `Akka.Serialization.Serializer`, send the resulting byte array as the message body, and deserialize received byte arrays using that serializer. Peers must agree on this representation; a plain AMQP string body is not the same format. `IAmqpSinkSettings<T>` and `IAmqpSourceSettings<T>` let applications supply different byte conversion/link behavior.

## Settings and ownership

| Settings | Connection behavior |
| --- | --- |
| `AddressSinkSettings<T>` / `AddressSourceSettings<T>` | Create their own AMQP connection and session when connecting. Permit initial connection retries. Call `CloseConnectionAsync` on each settings object after stopping its stream. |
| `NamedQueueSinkSettings<T>` / `NamedQueueSourceSettings<T>` | Use your existing `Amqp.Session`, link name and queue name. The application owns that session and connection; these settings do not retry initial connection creation. |

Stage shutdown closes its sender/receiver link. It does not automatically close the session or connection, including those created by address settings. Stop streams before closing their settings or borrowed clients. Address settings' initial connection retries do not provide reconnection after an established link disconnects; that disconnection fails the stream.

Source settings require a `credit` value. The SDK receiver starts when the source materializes and can buffer deliveries before downstream demand. The source accepts a successfully converted message **before emitting it**, so downstream processing happens after AMQP acceptance. Choose this delivery behavior deliberately; this API does not defer acceptance until your downstream work completes.

With default stopping supervision, a conversion error rejects the message and fails the source. A non-stopping source strategy leaves the failed message unsettled. The sink does not apply a supervision decider. The connector does not provide exactly-once processing or transactions spanning your application work.

## Local integration tests

From the repository root, with the pinned .NET SDK and Docker running Linux containers:

```shell
dotnet test src/Amqp/Akka.Streams.Amqp.V1.Tests/Akka.Streams.Amqp.V1.Tests.csproj -c Release --framework net10.0
```

The tests use `Testcontainers.ActiveMq`'s `ArtemisBuilder` with the official `apache/activemq-artemis:2.42.0` image. They discover the mapped AMQP-capable 61616 acceptor and remove the owned broker afterward. No custom broker image, fixed host port or separately installed service is required.

Coverage includes native AMQPNetLite peers, named/address settings, serialized Unicode payloads, acceptance/redelivery observations, link closure and completion-task failures. These results establish the exercised protocol behavior against the pinned Artemis instance; they do not establish other brokers' address policies, clustering, TLS or production recovery behavior. See [Artemis AMQP support](https://activemq.apache.org/components/artemis/documentation/latest/amqp.html) and [AMQPNetLite documentation](https://azure.github.io/amqpnetlite/).

## License

Apache-2.0
