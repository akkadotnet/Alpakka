using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.EventHub;
using Akka.Streams.Dsl;

Console.WriteLine("Starting event consumer");

var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__eventhubs");
if (connectionString is null)
    throw new Exception("The environment variable CONNECTIONSTRINGS__orders was not set.");


var system = ActorSystem.Create("ServiceBusProducer");
var materializer = system.Materializer();

var factory = new EventHub.Consumer.ProcessorFactory();

var processor =  EventHubSource.Create(factory)
    .SelectAsync(1, async evt =>
    {
        var data = evt.Data;
        if (!evt.HasEvent || data is null)
            return Done.Instance;
        var message = data.EventBody.ToString();
        Console.WriteLine($"Message from Partition: {evt.Partition.PartitionId} , Message: {message}");
        await evt.UpdateCheckpointAsync(); // usually checkpoint after processing batch of messages
        return Done.Instance;
    })
    .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
    .Run(materializer);

await system.WhenTerminated;
