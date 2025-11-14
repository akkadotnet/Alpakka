using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Azure.Messaging.ServiceBus;

namespace ServiceBusProducer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__orders");
        if (connectionString is null)
            throw new Exception("The environment variable CONNECTIONSTRINGS__orders was not set.");

        var system = ActorSystem.Create("ServiceBusProducer");
        var materializer = system.Materializer();

        var client = new ServiceBusClient(connectionString);
        var sender = client.CreateSender("orders");

        Console.WriteLine("Sending messages to the 'orders' topic...");

        await Source
            .Cycle(() => Enumerable.Range(1, 100).GetEnumerator())
            .Throttle(1, TimeSpan.FromMilliseconds(200), 1, ThrottleMode.Shaping)
            .Select(x => new ServiceBusMessage($"Sending message...{x}"))
            .WireTap(msg => Console.WriteLine($"Preparing to send: {msg.Body}"))
            .Grouped(10)
            .ToServiceBus(sender, materializer);

        await system.WhenTerminated;

    }
}