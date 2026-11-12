using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.EventHub;
using Akka.Streams.Dsl;

Console.WriteLine("Starting event consumer");

var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__eventhubs");
if (connectionString is null)
    throw new Exception("The environment variable CONNECTIONSTRINGS__eventhubs was not set.");


var system = ActorSystem.Create("EventHubConsumer");
var materializer = system.Materializer();

var factory = new EventHub.Consumer.ProcessorFactory();

var processor =  EventHubSource.Create(factory)
    .SelectAsync(1, async evt =>
    {
        if (!evt.HasEvent)
            return Done.Instance;
        var message = evt.Data!.EventBody.ToString();
        Console.WriteLine($"Message from Partition: {evt.Partition.PartitionId} , Message: {message}");
        await evt.UpdateCheckpointAsync(); // usually checkpoint after processing batch of messages
        return Done.Instance;
    })
    .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
    .Run(materializer);

await system.WhenTerminated;
