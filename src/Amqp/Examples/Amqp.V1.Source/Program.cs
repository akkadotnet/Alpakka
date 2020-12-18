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

namespace Amqp.V1.Source
{
    class Program
    {
        private const string QueueName = "akka.test";
        private const string ReceiverLinkName = "amqp-conn-test-sender";

        private static ActorSystem _system;
        private static ActorMaterializer _materializer;
        private static Serializer _serializer;
        private static Address _address;

        static async Task Main(string[] args)
        {
            _system = ActorSystem.Create("AMQP-System-Source");
            _materializer = ActorMaterializer.Create(_system);
            var serialization = _system.Serialization;
            _serializer = serialization.FindSerializerForType(typeof(string));
            _address = new Address("127.0.0.1", 5672, "guest", "guest", scheme: "AMQP");

            await UsingAddressSource();

            await _system.Terminate();
        }

        // The settings object manages the Session and Connection object
        private static async Task UsingAddressSource()
        {
            var settings = new AddressSourceSettings<string>(_address, ReceiverLinkName, QueueName, 200, _serializer);

            //create source
            var amqpSource = RestartSource.OnFailuresWithBackoff(
                () => {
                    Console.WriteLine("Start/Restart...");
                    return AmqpSource.Create(settings);
                },
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                0.2,
                short.MaxValue
            );

            //run source
            await amqpSource
                .Throttle(1, TimeSpan.FromSeconds(1), 10, ThrottleMode.Shaping)
                .RunForeach(Console.WriteLine, _materializer);

            Console.ReadKey();

            await settings.CloseConnectionAsync();
        }

        // The user manages the Session and Connection object
        private static async Task UsingNamedQueueSource()
        {
            Connection connection = null;
            Session session = null;

            var amqpSource = RestartSource.WithBackoff(() =>
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

                return AmqpSource.Create(new NamedQueueSourceSettings<string>(session, ReceiverLinkName, QueueName, 200, _serializer));
            },
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                0.2,
                short.MaxValue
            );

            //run source
            await amqpSource
                .Throttle(1, TimeSpan.FromSeconds(1), 10, ThrottleMode.Shaping)
                .RunForeach(Console.WriteLine, _materializer);

            Console.ReadKey();

            if (session != null)
                await session.CloseAsync();
            if (connection != null)
                await connection.CloseAsync();
        }
    }
}
