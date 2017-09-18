using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Amqp.Dsl;
using Akka.Streams.Dsl;
using Xunit;

namespace Akka.Streams.Amqp.Tests
{
    
    public class AmqpConnectorsTest : IDisposable
    {
        private ActorSystem _system;
        private IMaterializer _materializer;
        public AmqpConnectorsTest()
        {
            _system = ActorSystem.Create(GetType().Name);
            _materializer = _system.Materializer();
            
        }

        [Fact]
        public void PublishAndConsume()
        {
            var connectionSettings = AmqpConnectionDetails.Create("localhost", 5672).WithAutomaticRecoveryEnabled(true).WithNetworkRecoveryInterval(TimeSpan.FromSeconds(1));

            var exchange = ExchangeDeclaration.Create("logs", "topic");

            //queue declaration
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName).WithDurable(false).WithAutoDelete(true);

            

            //create sink
            var amqpSink = AmqpSink.CreateSimple(
                AmqpSinkSettings.Create(connectionSettings)
                .WithRoutingKey(queueName)
                .WithDeclarations(queueDeclaration));

            //create source
            int bufferSize = 10;
            var amqpSource = AmqpSource.Create(
                NamedQueueSourceSettings.Create(DefaultAmqpConnection.Instance, queueName)
                    .WithDeclarations(queueDeclaration),
                bufferSize);

            //run sink
            var input = new List<string> { "one", "two", "three", "four", "five" };
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _materializer).Wait();

            //run source
            var result =
                amqpSource.Select(m => m.Bytes.ToString(Encoding.UTF8))
                    .Take(input.Count)
                    .RunWith(Sink.Seq<string>(), _materializer);

            result.Wait(TimeSpan.FromSeconds(3));

            Assert.Equal(input, result.Result);


        }


        public void Dispose()
        {
            _system.Terminate().Wait();
        }
    }
}
