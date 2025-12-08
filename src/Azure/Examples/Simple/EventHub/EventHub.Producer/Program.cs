using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.EventHub;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

Console.WriteLine("Hello, World!");

var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__eventhubs");
if (connectionString is null)
    throw new Exception("The environment variable CONNECTIONSTRINGS__orders was not set.");


var system = ActorSystem.Create("ServiceBusProducer");
var materializer = system.Materializer();


var client = new EventHubProducerClient(connectionString, "orders");

await Source.From(Enumerable.Range(1, int.MaxValue))
                .Select(i => new EventData(Encoding.UTF8.GetBytes("Publishing order : " + i)))
                .WireTap(msg => Console.WriteLine($"Preparing to send: {Encoding.UTF8.GetString(msg.Body.ToArray())}"))
                .Grouped(10)
                .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                .ToEventHub(client, materializer);


await system.WhenTerminated;


