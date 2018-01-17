using System;
using System.Linq;
using System.Text;
using Akka.IO;
using Akka.Streams.Amqp.Dsl;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Amqp.Tests
{
    /// <summary>
    /// Needs a local running AMQP server on the default port with no password.
    /// </summary>
    /// <inheritdoc />
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
            ExchangeDeclaration.Create("logs", "topic");

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
            
            var rpcQueueNameTask = t.Item1;
            var probe = t.Item2;
            
            //#run-rpc-flow
            rpcQueueNameTask.Result.Should().NotBeNullOrWhiteSpace("RPC flow materializes into response queue name");

            var amqpSink = AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings));

            amqpSource
                .Select(msg => new OutgoingMessage(msg.Bytes.Concat(ByteString.FromString("a")), false, false, msg.Properties))
                .RunWith(amqpSink, _mat);

            probe.Request(5).ExpectNextUnorderedN(input.Select(s => ByteString.FromString(s + "a"))).ExpectComplete();
        }
        
        [Fact]
        public void Publish_from_one_source_and_consume_elements_with_multiple_sinks()
        {
            var queueName = "amqp-conn-it-spec-work-queues-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(
                AmqpSinkSettings.Create(_connectionSettings)
                    .WithRoutingKey(queueName)
                    .WithDeclarations(queueDeclaration));

            var input = new[] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat);

            var mergedSources = Source.FromGraph(GraphDsl.Create(b =>
            {
                const int count = 3;
                var merge = b.Add(new Merge<IncomingMessage>(count));
                for (var n = 0; n < count; n++)
                {
                    var source = b.Add(
                        AmqpSource.AtMostOnceSource(
                            NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration),
                            bufferSize: 1));

                    b.From(source.Outlet).To(merge.In(n));
                }

                return new SourceShape<IncomingMessage>(merge.Out);
            }));

            var result = mergedSources.Select(x => x.Bytes.ToString()).Take(input.Length).RunWith(Sink.Seq<string>(), _mat);
            result.Result.OrderBy(x => x).ToArray().Should().Equal(input.OrderBy(x => x).ToArray());
        }
    }
}