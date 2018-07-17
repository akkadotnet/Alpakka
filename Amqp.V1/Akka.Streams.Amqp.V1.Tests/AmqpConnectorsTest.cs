using Akka.Serialization;
using Akka.Streams.Amqp.V1.Dsl;
using Akka.Streams.Dsl;
using Amqp;
using System;
using Xunit;

namespace Akka.Streams.Amqp.V1.Tests
{
    public class AmqpConnectorsTest : Akka.TestKit.Xunit2.TestKit
    {
        private readonly Serializer serializer;
        private readonly Address address;
        private readonly ActorMaterializer materializer;

        public AmqpConnectorsTest()
        {
            materializer = ActorMaterializer.Create(Sys);
            var serialization = Sys.Serialization;

            serializer = serialization.FindSerializerForType(typeof(string));
            address = new Address("amqp://guest:guest@localhost:5673");
        }

        [Fact]
        public void Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process()
        {
            Connection connection = new Connection(address);
            Session session = new Session(connection);

            var queueName = "q1";
            var senderlinkName = "amqp-conn-test-sender";
            var receiverlinkName = "amqp-conn-test-sender";

            //create sink and source
            var amqpSink = AmpqSink<string>.Create(new NamedQueueSinkSettings<string>(session, senderlinkName, queueName, serializer));
            var amqpSource = AmpqSource<string>.Create(new NamedQueueSourceSettings<string>(session, receiverlinkName, queueName, 200, serializer));

            //run sink
            var input = new[] { "one", "two", "three", "four", "five" };
            Source.From(input).RunWith(amqpSink, materializer).Wait();
            
            //run source
            var result = amqpSource
                            .Take(input.Length)
                            .RunWith(Sink.Seq<string>(), materializer);

            result.Wait(TimeSpan.FromSeconds(3));
            Assert.Equal(input, result.Result);

            session.Close();
            connection.Close();
        }
    }
}
