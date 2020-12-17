using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Amqp.V1;
using Akka.Streams.Amqp.V1.Dsl;
using Akka.Streams.Dsl;

namespace Amqp.V1.Sink
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var sys = ActorSystem.Create("AMQP-System-Sink");
            var materializer = ActorMaterializer.Create(sys);
            var serialization = sys.Serialization;
            var serializer = serialization.FindSerializerForType(typeof(string));

            var address = new Address("127.0.0.1", 5672, "guest", "guest", scheme: "AMQP");

            var queueName = "akka.test";
            var senderLinkName = "amqp-conn-test-sender";

            var amqpSink = RestartSink.WithBackoff(() =>
                {
                    Console.WriteLine("Start/Restart...");
                    return AmqpSink.Create(
                        new AddressSinkSettings<string>(address, senderLinkName, queueName, serializer));
                },
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                0.2,
                short.MaxValue
            );

            //run sink
            var input = new[] { "one", "two", "three", "four", "five" };
            Source
                .Cycle(() => input.AsEnumerable().GetEnumerator())
                .Delay(TimeSpan.FromSeconds(1))
                .Select(s =>
                {
                    Console.WriteLine(s);
                    return s;
                })
                .RunWith(amqpSink, materializer);

            Console.ReadKey();

            await sys.Terminate();
        }
    }
}
