using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.ServiceBus;
using Azure.Messaging.ServiceBus;

namespace ServiceBusConsumer;

public class Program
{
    private static async Task Main(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__orders");
        if (connectionString is null)
            throw new Exception("The environment variable CONNECTIONSTRINGS__orders was not set.");

        var system = ActorSystem.Create("ServiceBusProducer");
        var materializer = system.Materializer();

        var client = new ServiceBusClient(connectionString);
        var receiver = client.CreateReceiver("orders", "shipping-subscriber");

        Console.WriteLine("Reading messages from the queue");
        await ServiceBusSource.Create<string>(receiver, msg => Encoding.UTF8.GetString(msg.Body.ToArray())).RunForeach(x => Console.WriteLine("Received {0}", x), materializer);

        await system.WhenTerminated;
    }
}