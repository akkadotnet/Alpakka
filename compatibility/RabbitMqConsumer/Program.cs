using System;
using System.Threading.Tasks;
using Akka;
using Akka.IO;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;
using RabbitMQ.Client;

internal static class Program
{
    private static async Task Main()
    {
        var connection = AmqpConnectionUri.Create(new Uri("amqp://guest:guest@localhost:5672/"));
        var original = QueueDeclaration.Create("old-consumer-queue");
        var durable = original.WithDurable(true);
        if (original.Durable || original.Exclusive || original.AutoDelete || !durable.Durable)
            throw new InvalidOperationException("Retained declaration defaults or immutability changed.");
        var settings = AmqpSinkSettings.Create(connection).WithRoutingKey(original.Name)
            .WithDeclarations(durable).WithWaitForConfirms(TimeSpan.FromSeconds(5));
        var sourceSettings = NamedQueueSourceSettings.Create(connection, original.Name).WithDeclarations(durable);
        Source<CommittableIncomingMessage, NotUsed> source = AmqpSource.CommittableSource(sourceSettings, 10);
        Source<IncomingMessage, NotUsed> atMostOnce = AmqpSource.AtMostOnceSource(sourceSettings, 10);
        Sink<OutgoingMessage, Task> sink = AmqpSink.Create(settings);
        Sink<ByteString, Task> simple = AmqpSink.CreateSimple(settings);
        Flow<(OutgoingMessage, string), string, Task> flow = AmqpFlow.Create<string>(settings);
        Flow<ByteString, ByteString, Task<string>> rpc = AmqpRpcFlow.CreateSimple(settings);
        if (source is null || atMostOnce is null || sink is null || simple is null || flow is null || rpc is null)
            throw new InvalidOperationException("A retained graph factory failed.");

        IBasicProperties? properties = null;
        var payload = ByteString.FromString("old-consumer-payload");
        var outgoing = new OutgoingMessage(payload, false, true, properties, original.Name);
        var incoming = new IncomingMessage(payload, Envelope.Create(42, true, "", original.Name), properties);
        var acked = false;
        var nacked = false;
        var committable = new CommittableIncomingMessage(incoming,
            multiple => { acked = multiple; return Task.CompletedTask; },
            (multiple, requeue) => { nacked = multiple && !requeue; return Task.CompletedTask; });
        await committable.Ack(multiple: true);
        await committable.Nack(multiple: true, requeue: false);
        if (!acked || !nacked || outgoing.Properties != properties || incoming.Properties != properties
            || !incoming.Envelope.Redeliver || incoming.Envelope.DeliveryTag != 42 || incoming.Bytes.ToString() != "old-consumer-payload")
            throw new InvalidOperationException("Retained message or settlement delegates changed behavior.");
        Console.WriteLine("Previously compiled RabbitMQ consumer passed (factory, message, defaults and settlement-delegate APIs).");
    }
}
