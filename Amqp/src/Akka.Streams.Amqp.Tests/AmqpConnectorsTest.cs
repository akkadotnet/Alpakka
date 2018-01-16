using System;
using System.Linq;
using System.Text;
using Akka.IO;
using Akka.Streams.Amqp.Dsl;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Xunit;

namespace Akka.Streams.Amqp.Tests
{
    public class AmqpConnectorsTest : Akka.TestKit.Xunit2.TestKit
    {
        private readonly AmqpConnectionDetails _connectionSettings;
        private readonly ActorMaterializer _mat;

        public AmqpConnectorsTest()
        {
            _mat = ActorMaterializer.Create(Sys);
            _connectionSettings = 
                AmqpConnectionDetails.Create("localhost", 5672)
                    .WithAutomaticRecoveryEnabled(true)
                    .WithNetworkRecoveryInterval(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process()
        {
            var exchange = ExchangeDeclaration.Create("logs", "topic");

            //queue declaration
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName).WithDurable(false).WithAutoDelete(true);

            //create sink
            var amqpSink = AmqpSink.CreateSimple(
                AmqpSinkSettings.Create(_connectionSettings)
                    .WithRoutingKey(queueName)
                    .WithDeclarations(queueDeclaration));

            //create source
            var amqpSource = AmqpSource.AtMostOnceSource(
                NamedQueueSourceSettings.Create(DefaultAmqpConnection.Instance, queueName).WithDeclarations(queueDeclaration),
                bufferSize: 10);

            //run sink
            var input = new [] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat).Wait();

            //run source
            var result =
                amqpSource.Select(m => m.Bytes.ToString(Encoding.UTF8))
                    .Take(input.Length)
                    .RunWith(Sink.Seq<string>(), _mat);

            result.Wait(TimeSpan.FromSeconds(3));
            Assert.Equal(input, result.Result);
        }
        
        [Fact]
        public void Publish_via_RPC_and_then_consume_through_a_simple_queue_again_in_the_same_process()
        {
            var queueName = "amqp-conn-it-spec-rpc-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            //#create-rpc-flow
            var amqpRpcFlow = AmqpRpcFlow.CreateSimple(
                AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));
            
            //#create-rpc-flow
            var amqpSource = AmqpSource.AtMostOnceSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName), bufferSize: 1);

            var input = new[] {"one", "two", "three", "four", "five"};
            
            //#run-rpc-flow
            var t = Source.From(input)
                .Select(ByteString.FromString)
                .ViaMaterialized(amqpRpcFlow, Keep.Right)
                .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                .Run(_mat);
            
            var rpcQueueF = t.Item1;
            var probe = t.Item2;
            
            //#run-rpc-flow
            rpcQueueF.Wait();

            var amqpSink = AmqpSink.Create(AmqpSinkSettings.Create(_connectionSettings));

            amqpSource
                .Select(b => OutgoingMessage.Create(b.Bytes.Concat(ByteString.FromString("a")), false, false, b.Properties))
                .RunWith(amqpSink, _mat);

            probe.Request(5).ExpectNextUnorderedN(input.Select(s => ByteString.FromString(s + "a"))).ExpectComplete();
        }
    }
}