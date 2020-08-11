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

            _address = new Address(_fixture.HostName, _fixture.AmqpPort, _fixture.UserName, _fixture.Password, scheme: "AMQP");
        }

        [Fact]
        public async Task Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process()
        {
            Connection.DisableServerCertValidation = true;
            Trace.TraceLevel = TraceLevel.Frame;
            Trace.TraceListener = (l, f, a) =>
                Output.WriteLine(DateTime.Now.ToString("[hh:mm:ss.fff]") + " " + string.Format(f, a));

            var connection = new Connection(_address);
            var session = new Session(connection);

            var queueName = "simple-v1-queue-test" + Guid.NewGuid();
            var senderLinkName = "amqp-v1-conn-test-sender";
            var receiverLinkName = "amqp-v1-conn-test-receiver";

            //create sink and source
            var amqpSink = AmpqSink.Create(new NamedQueueSinkSettings<string>(session, senderLinkName, queueName, _serializer));
            var amqpSource = AmpqSource.Create(new NamedQueueSourceSettings<string>(session, receiverLinkName, queueName, 200, _serializer));

            //run sink
            var input = new[] { "one", "two", "three", "four", "five" };
            await Source.From(input).RunWith(amqpSink, _materializer);
            
            //run source
            var result = amqpSource
                            .Take(input.Length)
                            .RunWith(Sink.Seq<string>(), _materializer);

            await result;
            Assert.True(result.IsCompleted);
            Assert.Equal(input, result.Result);

            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }
}
