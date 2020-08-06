using Akka.Serialization;
using Akka.Streams.Amqp.V1.Dsl;
using Akka.Streams.Dsl;
using Amqp;
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Amqp.Tests;
using Amqp.Sasl;
using Xunit;
using Xunit.Abstractions;
using Address = Amqp.Address;

namespace Akka.Streams.Amqp.V1.Tests
{
    [Collection("AmqpSpec")]
    public class AmqpConnectorsTest : Akka.TestKit.Xunit2.TestKit
    {
        private readonly Serializer _serializer;
        private readonly Address _address;
        private readonly ActorMaterializer _materializer;
        private readonly AmqpFixture _fixture;

        public AmqpConnectorsTest(AmqpFixture fixture, ITestOutputHelper output) :
            base((ActorSystem)null, output)
        {
            _materializer = ActorMaterializer.Create(Sys);
            _serializer = Sys.Serialization.FindSerializerForType(typeof(string));
            _fixture = fixture;

            _address = _fixture.Address;
        }

        [Fact]
        public async Task Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process()
        {
            var connection = await Connection.Factory.CreateAsync(_address);
            var session = new Session(connection);

            var queueName = "simple-v1-queue-test" + Guid.NewGuid();
            var senderLinkName = "amqp-v1-conn-test-sender";
            var receiverLinkName = "amqp-v1-conn-test-receiver";

            //create sink and source
            var amqpSink = AmpqSink.Create(new NamedQueueSinkSettings<string>(session, senderLinkName, queueName, _serializer));
            var amqpSource = AmpqSource.Create(new NamedQueueSourceSettings<string>(session, receiverLinkName, queueName, 200, _serializer));

            //run sink
            var input = new[] { "one", "two", "three", "four", "five" };
            Source.From(input).RunWith(amqpSink, _materializer).Wait();
            
            //run source
            var result = amqpSource
                            .Take(input.Length)
                            .RunWith(Sink.Seq<string>(), _materializer);

            result.Wait(TimeSpan.FromSeconds(30));
            Assert.True(result.IsCompleted);
            Assert.Equal(input, result.Result);

            session.Close();
            connection.Close();
        }
    }
}
