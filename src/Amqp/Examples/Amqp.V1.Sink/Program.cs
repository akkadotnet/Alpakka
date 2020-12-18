using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Amqp.V1;
using Akka.Streams.Amqp.V1.Dsl;
using Akka.Streams.Dsl;

namespace Amqp.V1.Sink
{
    class Program
    {
        private const string QueueName = "akka.test";
        private const string SenderLinkName = "amqp-conn-test-sender";

        private static ActorSystem _system;
        private static ActorMaterializer _materializer;
        private static Serializer _serializer;
        private static Address _address;

        static async Task Main(string[] args)
        {
            _system = ActorSystem.Create("AMQP-System-Sink");
            _materializer = ActorMaterializer.Create(_system);
            var serialization = _system.Serialization;
            _serializer = serialization.FindSerializerForType(typeof(string));
            _address = new Address("127.0.0.1", 5672, "guest", "guest", scheme: "AMQP");

            await UsingAddressSink();

            await _system.Terminate();
        }

        // The settings object manages the Session and Connection object
        private static async Task UsingAddressSink()
        {
            var settings = new AddressSinkSettings<string>(_address, SenderLinkName, QueueName, _serializer);

            // create sink
            var amqpSink = RestartSink.WithBackoff(() =>
                {
                    Console.WriteLine("Start/Restart...");
                    return AmqpSink.Create(settings);
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
                .RunWith(amqpSink, _materializer);

            Console.ReadKey();

            await settings.CloseConnectionAsync();
        }

        // The user manages the Session and Connection object
        private static async Task UsingNamedQueueSink()
        {
            Connection connection = null;
            Session session = null;

            // create sink
            var amqpSink = RestartSink.WithBackoff(() =>
                {
                    Console.WriteLine("Start/Restart...");
                    try
                    {
                        if (connection == null || connection.IsClosed)
                            connection = new Connection(_address);

                        if (session == null || session.IsClosed)
                            session = new Session(connection);
                    }
                    catch
                    {
                        connection?.Close();
                        session?.Close();

                        connection = null;
                        session = null;
                    }

                    return AmqpSink.Create(new NamedQueueSinkSettings<string>(session, SenderLinkName, QueueName, _serializer));
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
                .RunWith(amqpSink, _materializer);

            Console.ReadKey();

            if (session != null)
                await session.CloseAsync();
            if (connection != null)
                await connection.CloseAsync();
        }
    }
}
