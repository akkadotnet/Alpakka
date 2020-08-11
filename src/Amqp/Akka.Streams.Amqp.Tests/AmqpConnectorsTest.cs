using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Amqp.Dsl;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Amqp.Tests
{
    /// <inheritdoc />
    [Collection("AmqpSpec")]
    public class AmqpConnectorsTest : Akka.TestKit.Xunit2.TestKit
    {
        private readonly AmqpConnectionDetails _connectionSettings;
        private readonly ActorMaterializer _mat;
        private readonly AmqpFixture _fixture;

        public AmqpConnectorsTest(AmqpFixture fixture, ITestOutputHelper output) : 
            base((ActorSystem)null, output)
        {
            _mat = ActorMaterializer.Create(Sys);
            _connectionSettings =
                AmqpConnectionDetails
                    .Create(fixture.HostName, fixture.AmqpPort)
                    .WithCredentials(AmqpCredentials.Create(fixture.UserName, fixture.Password))
                    .WithAutomaticRecoveryEnabled(true)
                    .WithNetworkRecoveryInterval(TimeSpan.FromSeconds(1));
            _fixture = fixture;
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
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration),
                bufferSize: 10);

            //run sink
            var input = new[] {"one", "two", "three", "four", "five"};
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

            probe
                .Request(5)
                .ExpectNextUnorderedN(input.Select(s => ByteString.FromString(s + "a")))
                .ExpectComplete();
        }

        [Fact]
        public void Publish_via_RPC_which_expects_2_responses_per_message_and_then_consume_through_a_simple_queue_again_in_the_same_process()
        {
            var queueName = "amqp-conn-it-spec-rpc-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpRpcFlow = AmqpRpcFlow.CreateSimple(
                AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration), repliesPerMessage: 2);

            var amqpSource = AmqpSource.AtMostOnceSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName), bufferSize: 1);

            var input = new[] {"one", "two", "three", "four", "five"};

            var t =
                Source.From(input)
                      .Select(ByteString.FromString)
                      .ViaMaterialized(amqpRpcFlow, Keep.Right)
                      .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                      .Run(_mat);

            var rpcQueueF = t.Item1;
            var probe = t.Item2;
            rpcQueueF.Result.Should().NotBeNullOrWhiteSpace("RPC flow materializes into response queue name");

            var amqpSink = AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings));

            amqpSource
                .SelectMany(b =>
                    new[]
                    {
                        new OutgoingMessage(b.Bytes.Concat(ByteString.FromString("a")), false, false, b.Properties),
                        new OutgoingMessage(b.Bytes.Concat(ByteString.FromString("aa")), false, false, b.Properties)
                    })
                .RunWith(amqpSink, _mat);

            probe
                .Request(10)
                .ExpectNextUnorderedN(input.SelectMany(s => new[] {ByteString.FromString(s + "a"), ByteString.FromString(s + "aa")}))
                .ExpectComplete();
        }

        [Fact]
        public void Correctly_close_a_AmqpRpcFlow_when_stream_is_closed_without_passing_any_elements()
        {
            Source.Empty<ByteString>()
                  .Via(AmqpRpcFlow.CreateSimple(AmqpSinkSettings.Create(_connectionSettings)))
                  .RunWith(this.SinkProbe<ByteString>(), _mat)
                  .EnsureSubscription()
                  .ExpectComplete();
        }

        [Fact]
        public void Handle_missing_reply_to_header_correctly()
        {
            var outgoingMessage = new OutgoingMessage(ByteString.Empty, false, false);

            Source
                .Single(outgoingMessage)
                .WatchTermination(Keep.Right)
                .To(AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings)))
                .Run(_mat)
                .Wait();

            try
            {
                Source
                    .Single(outgoingMessage)
                    .ToMaterialized(AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings, failIfReplyToMissing: true)), Keep.Right)
                    .Run(_mat)
                    .Wait();

                throw new Exception("Expected exception but there is no one");
            }
            catch (AggregateException ex)
            {
                ex.InnerException?.Message.Should().Be("Reply-to header was not set");
            }
        }

        [Fact]
        public void Not_fail_on_a_fast_producer_and_a_slow_consumer()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-2-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpSource = AmqpSource.AtMostOnceSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 2);

            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));
            var publisher = this.CreatePublisherProbe<ByteString>();
            var subscriber = this.CreateSubscriberProbe<IncomingMessage>();
            amqpSink.AddAttributes(Attributes.CreateInputBuffer(1, 1)).RunWith(Source.FromPublisher(publisher), _mat);
            amqpSource.AddAttributes(Attributes.CreateInputBuffer(1, 1)).RunWith(Sink.FromSubscriber(subscriber), _mat);

            // note that this essentially is testing rabbitmq just as much as it tests our sink and source
            publisher.EnsureSubscription();
            subscriber.EnsureSubscription();

            publisher.ExpectRequest().Should().Be(1);
            publisher.SendNext(ByteString.FromString("one"));

            publisher.ExpectRequest();
            publisher.SendNext(ByteString.FromString("two"));

            publisher.ExpectRequest();
            publisher.SendNext(ByteString.FromString("three"));

            publisher.ExpectRequest();
            publisher.SendNext(ByteString.FromString("four"));

            publisher.ExpectRequest();
            publisher.SendNext(ByteString.FromString("five"));

            subscriber.Request(4);
            subscriber.ExpectNext().Bytes.ToString().Should().Be("one");
            subscriber.ExpectNext().Bytes.ToString().Should().Be("two");
            subscriber.ExpectNext().Bytes.ToString().Should().Be("three");
            subscriber.ExpectNext().Bytes.ToString().Should().Be("four");

            subscriber.Request(1);
            subscriber.ExpectNext().Bytes.ToString().Should().Be("five");

            subscriber.Cancel();
            publisher.SendComplete();
        }

        [Fact]
        public void Pub_sub_from_one_source_with_multiple_sinks()
        {
            // with pubsub we arrange one exchange which the sink writes to and then one queue for each source which subscribes to the
            // exchange - all this described by the declarations

            //#exchange-declaration
            var exchangeName = "amqp-conn-it-spec-pub-sub-" + Environment.TickCount;
            var exchangeDeclaration = ExchangeDeclaration.Create(exchangeName, "fanout");

            //#create-exchange-sink
            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithExchange(exchangeName).WithDeclarations(exchangeDeclaration));

            //#create-exchange-source
            const int fanoutSize = 4;

            var mergedSources =
                Enumerable.Range(0, fanoutSize)
                          .Aggregate(Source.Empty<(int, string)>(), (source, fanoutBranch) =>
                              source.Merge(
                                  AmqpSource.AtMostOnceSource(
                                                TemporaryQueueSourceSettings.Create(_connectionSettings, exchangeName).WithDeclarations(exchangeDeclaration),
                                                bufferSize: 1)
                                            .Select(msg => (branch: fanoutBranch, message: msg.Bytes.ToString())))
                          );

            var completion = new TaskCompletionSource<Done>();

            mergedSources.RunWith(Sink.Aggregate<(int branch, string msg), ImmutableHashSet<int>>(ImmutableHashSet.Create<int>(), (seen, t) =>
            {
                if (seen.Count == fanoutSize) completion.SetResult(Done.Instance);
                return seen.Add(t.branch);
            }), _mat);

            Sys.Scheduler.Advanced.ScheduleOnce(
                TimeSpan.FromSeconds(10),
                () => completion.TrySetException(new Exception("Did not get at least one element from every fanout branch")));

            Source.Repeat("stuff").Select(ByteString.FromString).RunWith(amqpSink, _mat);
            completion.Task.Result.Should().Be(Done.Instance);
        }

        [Fact]
        public void Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process_without_autoAck()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));

            //#create-source-withoutautoack
            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration),
                bufferSize: 10);

            //#create-source-withoutautoack

            var input = new[] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat).Wait();

            //#run-source-withoutautoack
            var result = amqpSource
                         .SelectAsync(1, async cm =>
                         {
                             await cm.Ack();
                             return cm;
                         })
                         .Take(input.Length)
                         .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            //#run-source-withoutautoack
            result.Result.Select(x => x.Message.Bytes.ToString()).Should().Equal(input);
        }

        [Fact]
        public void Republish_message_without_autoAck_if_nack_is_sent()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));

            var input = new[] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat).Wait();

            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            //#run-source-withoutautoack-and-nack
            var result1 = amqpSource
                          .Take(input.Length)
                          .SelectAsync(1, async cm =>
                          {
                              await cm.Nack();
                              return cm;
                          })
                          .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            //#run-source-withoutautoack-and-nack
            result1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

            var result2 = amqpSource
                          .SelectAsync(1, async cm =>
                          {
                              await cm.Ack();
                              return cm;
                          })
                          .Take(input.Length)
                          .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            result2.Result.Select(x => x.Message.Bytes.ToString()).Should().Equal(input);
        }

        [Fact]
        public void Keep_connection_open_if_downstream_closes_and_there_are_pending_acks()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));

            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            var input = new[] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat).Wait();
            var result = amqpSource.Take(input.Length).RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            foreach (var cm in result.Result)
            {
                cm.Ack().Wait();
            }
        }

        [Fact]
        public void Not_republish_message_without_autoAck_false_if_nack_is_sent()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));
            var input = new[] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat).Wait();

            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            var result1 = amqpSource
                          .SelectAsync(1, async cm =>
                          {
                              await cm.Nack(requeue: false);
                              return cm;
                          })
                          .Take(input.Length)
                          .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            result1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

            var result2 = amqpSource
                          .SelectAsync(1, async cm =>
                          {
                              await cm.Ack();
                              return cm;
                          })
                          .Take(input.Length)
                          .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            result2.Wait(TimeSpan.FromSeconds(1)).Should().BeFalse();
        }

        [Fact]
        public void Publish_via_RPC_and_then_consume_through_a_simple_queue_again_in_the_same_process_without_autoAck()
        {
            var queueName = "amqp-conn-it-spec-rpc-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var input = new[] {"one", "two", "three", "four", "five"};

            var amqpRpcFlow = AmqpRpcFlow.CommittableFlow(
                AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            var t =
                Source.From(input)
                      .Select(ByteString.FromString)
                      .Select(bytes => new OutgoingMessage(bytes, false, false))
                      .ViaMaterialized(amqpRpcFlow, Keep.Right)
                      .SelectAsync(1, async cm =>
                      {
                          await cm.Ack();
                          return cm.Message;
                      })
                      .ToMaterialized(this.SinkProbe<IncomingMessage>(), Keep.Both)
                      .Run(_mat);

            var rpcQueueF = t.Item1;
            var probe = t.Item2;
            rpcQueueF.Wait();

            var amqpSink = AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings));

            var amqpSource = AmqpSource.AtMostOnceSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName), bufferSize: 1);

            amqpSource
                .Select(b => new OutgoingMessage(b.Bytes, false, false, b.Properties))
                .RunWith(amqpSink, _mat);

            probe.ToStrict(TimeSpan.FromSeconds(3)).Select(x => x.Bytes.ToString()).Should().Equal(input);
        }

        [Fact]
        public void Set_routing_key_per_message_and_consume_them_in_the_same_process()
        {
            string GetRoutingKey(string s) => $"key.{s}";

            var exchangeName = "amqp.topic." + Environment.TickCount;
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var exchangeDeclaration = ExchangeDeclaration.Create(exchangeName, "topic");
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var bindingDeclaration = BindingDeclaration.Create(queueName, exchangeName).WithRoutingKey(GetRoutingKey("*"));

            var amqpSink = AmqpSink.Create(
                AmqpSinkSettings.Create(_connectionSettings)
                                .WithExchange(exchangeName)
                                .WithDeclarations(exchangeDeclaration, queueDeclaration, bindingDeclaration));

            var amqpSource = AmqpSource.AtMostOnceSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(exchangeDeclaration, queueDeclaration, bindingDeclaration),
                bufferSize: 10);

            var input = new[] {"one", "two", "three", "four", "five"};
            var routingKeys = input.Select(GetRoutingKey);

            Source.From(input)
                  .Select(s => new OutgoingMessage(ByteString.FromString(s), false, false, routingKey: GetRoutingKey(s)))
                  .RunWith(amqpSink, _mat)
                  .Wait();

            var result = amqpSource
                         .Take(input.Length)
                         .RunWith(Sink.Seq<IncomingMessage>(), _mat)
                         .Result;

            result.Select(x => x.Envelope.RoutingKey).Should().Equal(routingKeys);
            result.Select(x => x.Bytes.ToString()).Should().Equal(input);
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

        [Fact]
        public void Publish_elements_with_flow_then_consume_them_with_source()
        {
            ExchangeDeclaration.Create("logs", "topic");

            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName).WithDurable(false).WithAutoDelete(true);

            var amqpFlow =
                AmqpFlow.Create<string>(
                    AmqpSinkSettings.Create(_connectionSettings)
                                    .WithRoutingKey(queueName)
                                    .WithDeclarations(queueDeclaration));


            var input = new[] {"one", "two", "three", "four", "five"};

            var passedThrough =
                Source.From(input)
                      .Select(x => (new OutgoingMessage(ByteString.FromString(x), true, true), x))
                      .Via(amqpFlow)
                      .RunWith(Sink.Seq<string>(), _mat)
                      .Result
                      .ToArray();

            Assert.Equal(input, passedThrough, StringComparer.InvariantCulture);

            var consumed =
                AmqpSource.AtMostOnceSource(
                              NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration),
                              bufferSize: 10)
                          .Select(m => m.Bytes.ToString(Encoding.UTF8))
                          .Take(input.Length)
                          .RunWith(Sink.Seq<string>(), _mat)
                          .Result
                          .ToArray();

            Assert.Equal(input, consumed, StringComparer.InvariantCulture);
        }

        [Fact]
        public void Correctly_close_a_AmqpFlow_when_stream_is_closed_without_passing_any_elements()
        {
            Source.Empty<(OutgoingMessage, int)>()
                  .Via(AmqpFlow.Create<int>(AmqpSinkSettings.Create(_connectionSettings)))
                  .RunWith(this.SinkProbe<int>(), _mat)
                  .EnsureSubscription()
                  .ExpectComplete();
        }

        [Fact]
        public async Task Set_routing_key_per_message_while_publishing_with_flow_and_consume_them_in_the_same_process()
        {
            string GetRoutingKey(string s) => $"key.{s}";

            var exchangeName = "amqp.topic." + Environment.TickCount;
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var exchangeDeclaration = ExchangeDeclaration.Create(exchangeName, "topic");
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var bindingDeclaration = BindingDeclaration.Create(queueName, exchangeName).WithRoutingKey(GetRoutingKey("*"));

            var amqpFlow = AmqpFlow.Create<string>(
                AmqpSinkSettings.Create(_connectionSettings)
                                .WithExchange(exchangeName)
                                .WithDeclarations(exchangeDeclaration, queueDeclaration, bindingDeclaration));

            var amqpSource = AmqpSource.AtMostOnceSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(exchangeDeclaration, queueDeclaration, bindingDeclaration),
                bufferSize: 10);

            var input = new[] {"one", "two", "three", "four", "five"};
            var routingKeys = input.Select(GetRoutingKey);

            await Source.From(input)
                  .Select(s => (new OutgoingMessage(ByteString.FromString(s), false, false, routingKey: GetRoutingKey(s)), s))
                  .Via(amqpFlow)
                  .RunWith(Sink.Ignore<string>(), _mat);

            var result = await amqpSource
                    .Take(input.Length)
                    .RunWith(Sink.Seq<IncomingMessage>(), _mat);

            result.Select(x => x.Envelope.RoutingKey).Should().Equal(routingKeys);
            result.Select(x => x.Bytes.ToString()).Should().Equal(input);
        }

        [Fact]
        public void Declare_connection_that_does_not_require_server_acks()
        {
            var queueName = "amqp-conn-it-spec-fire-and-forget-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings)
                .WithRoutingKey(queueName)
                .WithDeclarations(queueDeclaration));

            var amqpSource = AmqpSource.CommittableSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName)
                .WithAckRequired(false)
                .WithDeclarations(queueDeclaration), bufferSize: 10);

            var input = new[] {"one", "two", "three", "four", "five"};
            Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat).Wait();

            var result = amqpSource.Take(input.Length).RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat).Result;
            result.Select(x => x.Message.Bytes.ToString(Encoding.UTF8)).Should().Equal(input);
        }
    }
}