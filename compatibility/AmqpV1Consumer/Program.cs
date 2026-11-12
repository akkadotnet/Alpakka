using System;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams.Amqp.V1;
using Akka.Streams.Amqp.V1.Dsl;
using Akka.Streams.Dsl;
using Amqp;
using Address = Amqp.Address;

internal static class Program
{
    private static async Task Main()
    {
        var system = ActorSystem.Create("old-amqp-v1-consumer");
        try
        {
            var address = new Address("amqp://localhost:5672");
            var serializer = system.Serialization.FindSerializerForType(typeof(string));
            var sourceSettings = new AddressSourceSettings<string>(address, "source", "queue", 1, serializer);
            var sinkSettings = new AddressSinkSettings<string>(address, "sink", "queue", serializer);
            Source<string, NotUsed> source = AmqpSource.Create(sourceSettings);
            Sink<string, Task> sink = AmqpSink.Create(sinkSettings);
            var sourceStage = new AmqpSourceStage<string>(sourceSettings);
            var sinkStage = new AmqpSinkStage<string>(sinkSettings);
            if (source is null || sink is null || sourceStage.Out is null || sinkStage.In is null)
                throw new InvalidOperationException("A retained API did not produce a stream stage.");

            var bytes = sinkSettings.GetBytes("old-consumer-payload");
            var decoded = sourceSettings.Convert(new Message(bytes));
            if (decoded != "old-consumer-payload")
                throw new InvalidOperationException("The retained serialization APIs changed behavior.");
        }
        finally
        {
            await system.Terminate();
        }

        Console.WriteLine("Previously compiled AMQP 1.0 consumer passed (factory and serialization APIs).");
    }
}
