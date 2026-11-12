using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Azure.Messaging.ServiceBus;

internal static class Program
{
    private static async Task Main()
    {
        var connection = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey="
                         + Convert.ToBase64String(new byte[32]);
        await using var client = new ServiceBusClient(connection);
        await using var receiver = client.CreateReceiver("queue");
        await using var sender = client.CreateSender("queue");

        Source<ServiceBusReceivedMessage, NotUsed> raw = ServiceBusSource.Create(receiver);
        Source<string, NotUsed> extracted = ServiceBusSource.Create(receiver, message => message.MessageId);
        Source<string, NotUsed> typed = ServiceBusSource.Create<string>(receiver);
        Sink<IEnumerable<ServiceBusMessage>, Task> sink = ServiceBusSink.Create(sender);
        if (raw is null || extracted is null || typed is null || sink is null)
            throw new InvalidOperationException("A retained API did not produce a stream stage.");

        var system = ActorSystem.Create("old-service-bus-consumer");
        try
        {
            await Source.Empty<IEnumerable<ServiceBusMessage>>()
                .ToServiceBus(sender, system.Materializer());
        }
        finally
        {
            await system.Terminate();
        }

        Console.WriteLine("Previously compiled Service Bus consumer passed.");
    }
}
