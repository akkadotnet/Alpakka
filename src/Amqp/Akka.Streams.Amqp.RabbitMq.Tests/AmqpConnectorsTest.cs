using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Extensions;
using Xunit;
using Xunit.Abstractions;
using System.Threading;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.RabbitMq;

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
            base(output:output)
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

        private static async Task CompleteWithin(Task task, TimeSpan timeout)
        {
            Assert.True(await task.AwaitWithTimeout(timeout));
            await task;
        }

        private static async Task<T> CompleteWithin<T>(Task<T> task, TimeSpan timeout)
            => await task.WithTimeout(timeout);

        [Fact]
        public async Task Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process()
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
            await CompleteWithin(Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            //run source
            var result = await CompleteWithin(amqpSource.Select(m => m.Bytes.ToString(Encoding.UTF8))
                        .Take(input.Length)
                        .RunWith(Sink.Seq<string>(), _mat), TimeSpan.FromSeconds(3));

            Assert.Equal(input, result);
        }

        [Fact]
        public async Task Publish_via_RPC_and_then_consume_through_a_simple_queue_again_in_the_same_process()
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
            var (rpcQueueNameTask, probe) = Source.From(input)
                          .Select(ByteString.FromString)
                          .ViaMaterialized(amqpRpcFlow, Keep.Right)
                          .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                          .Run(_mat);

            //#run-rpc-flow
            var result = await CompleteWithin(rpcQueueNameTask, TimeSpan.FromSeconds(15));
            Assert.False(string.IsNullOrWhiteSpace(result), "RPC flow materializes into response queue name");

            var amqpSink = AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings));

            var task = amqpSource
                .Take(input.Length)
                .Select(msg => new OutgoingMessage(msg.Bytes.Concat(ByteString.FromString("a")), false, false, msg.Properties))
                .RunWith(amqpSink, _mat);

            probe
                .Request(5)
                .ExpectNextUnorderedN(input.Select(s => ByteString.FromString(s + "a")))
                .ExpectComplete();

            await CompleteWithin(task, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task Publish_via_RPC_which_expects_2_responses_per_message_and_then_consume_through_a_simple_queue_again_in_the_same_process()
        {
            var queueName = "amqp-conn-it-spec-rpc-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpRpcFlow = AmqpRpcFlow.CreateSimple(
                AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration), repliesPerMessage: 2);

            var amqpSource = AmqpSource.AtMostOnceSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName), bufferSize: 1);

            var input = new[] {"one", "two", "three", "four", "five"};

            var (rpcQueueFTask, probe) =
                Source.From(input)
                      .Select(ByteString.FromString)
                      .ViaMaterialized(amqpRpcFlow, Keep.Right)
                      .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                      .Run(_mat);

            var result = await CompleteWithin(rpcQueueFTask, TimeSpan.FromSeconds(15));
            Assert.False(string.IsNullOrWhiteSpace(result), "RPC flow materializes into response queue name");

            var amqpSink = AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings));

            var task = amqpSource
                .Take(input.Length)
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

            await CompleteWithin(task, TimeSpan.FromSeconds(3));
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
        public async Task Handle_missing_reply_to_header_correctly()
        {
            var outgoingMessage = new OutgoingMessage(ByteString.Empty, false, false);

            await CompleteWithin(Source
                        .Single(outgoingMessage)
                        .WatchTermination(Keep.Right)
                        .To(AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings)))
                        .Run(_mat), TimeSpan.FromSeconds(3));

            await Assert.ThrowsAnyAsync<Exception>(async () => await Source
                .Single(outgoingMessage)
                .ToMaterialized(AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings, failIfReplyToMissing: true)), Keep.Right)
                .Run(_mat));
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

            Assert.Equal(1, publisher.ExpectRequest());
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
            Assert.Equal("one", subscriber.ExpectNext().Bytes.ToString());
            Assert.Equal("two", subscriber.ExpectNext().Bytes.ToString());
            Assert.Equal("three", subscriber.ExpectNext().Bytes.ToString());
            Assert.Equal("four", subscriber.ExpectNext().Bytes.ToString());

            subscriber.Request(1);
            Assert.Equal("five", subscriber.ExpectNext().Bytes.ToString());

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

            var seenBranches = ImmutableHashSet.Create<int>();
            mergedSources.RunForeach(e => seenBranches = seenBranches.Add(e.Item1), _mat);

            Source.Repeat("stuff").Select(ByteString.FromString).RunWith(amqpSink, _mat);
            
            // wait for each branch to be discovered, one by one
            foreach (var expectedSeenCount in Enumerable.Range(1, fanoutSize - 1))
            {
                AwaitCondition(() => seenBranches.Count >= expectedSeenCount, TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task Publish_and_consume_elements_through_a_simple_queue_again_in_the_same_process_without_autoAck()
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
            await CompleteWithin(Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            //#run-source-withoutautoack
            var task = amqpSource
                         .SelectAsync(1, async cm =>
                         {
                             await cm.Ack();
                             return cm;
                         })
                         .Take(input.Length)
                         .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            //#run-source-withoutautoack
            var result = await CompleteWithin(task, TimeSpan.FromSeconds(3));
            Assert.Equal(input, result.Select(x => x.Message.Bytes.ToString()));
        }

        [Fact]
        public async Task Republish_message_without_autoAck_if_nack_is_sent()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));

            var input = new[] {"one", "two", "three", "four", "five"};
            await CompleteWithin(Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            //#run-source-withoutautoack-and-nack
            await CompleteWithin(amqpSource
                    .Take(input.Length)
                    .SelectAsync(1, async cm =>
                    {
                        await cm.Nack();
                        return cm;
                    })
                    .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat), TimeSpan.FromSeconds(3));
            //#run-source-withoutautoack-and-nack

            var result = await CompleteWithin(amqpSource
                    .SelectAsync(1, async cm =>
                    {
                        await cm.Ack();
                        return cm;
                    })
                    .Take(input.Length)
                    .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat), TimeSpan.FromSeconds(3));
            Assert.Equal(input, result.Select(x => x.Message.Bytes.ToString()));
        }

        [Fact]
        public async Task Keep_connection_open_if_downstream_closes_and_there_are_pending_acks()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));

            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            var input = new[] {"one", "two", "three", "four", "five"};
            await CompleteWithin(Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            var result = await CompleteWithin(amqpSource.Take(input.Length).RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat), TimeSpan.FromSeconds(3));
            foreach (var cm in result)
            {
                await CompleteWithin(cm.Ack(), TimeSpan.FromSeconds(3));
            }
        }

        [Fact]
        public async Task Not_republish_message_without_autoAck_false_if_nack_is_sent()
        {
            var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration));
            var input = new[] {"one", "two", "three", "four", "five"};
            await CompleteWithin(Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            var amqpSource = AmqpSource.CommittableSource(
                NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            await CompleteWithin(amqpSource
                    .SelectAsync(1, async cm =>
                    {
                        await cm.Nack(requeue: false);
                        return cm;
                    })
                    .Take(input.Length)
                    .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat), TimeSpan.FromSeconds(3));

            var task = amqpSource
                          .SelectAsync(1, async cm =>
                          {
                              await cm.Ack();
                              return cm;
                          })
                          .Take(input.Length)
                          .RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat);

            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public async Task Publish_via_RPC_and_then_consume_through_a_simple_queue_again_in_the_same_process_without_autoAck()
        {
            var queueName = "amqp-conn-it-spec-rpc-queue-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var input = new[] {"one", "two", "three", "four", "five"};

            var amqpRpcFlow = AmqpRpcFlow.CommittableFlow(
                AmqpSinkSettings.Create(_connectionSettings).WithRoutingKey(queueName).WithDeclarations(queueDeclaration), bufferSize: 10);

            var (rpcQueueFTask, probe) =
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

            await CompleteWithin(rpcQueueFTask, TimeSpan.FromSeconds(15));

            var amqpSink = AmqpSink.ReplyTo(AmqpReplyToSinkSettings.Create(_connectionSettings));

            var amqpSource = AmqpSource.AtMostOnceSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName), bufferSize: 1);

            await CompleteWithin(amqpSource
                    .Take(input.Length)
                    .Select(b => new OutgoingMessage(b.Bytes, false, false, b.Properties))
                    .RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            var results = new System.Collections.Generic.List<IncomingMessage>();
            await foreach (var item in probe.ToStrictAsync(TimeSpan.FromSeconds(3)))
                results.Add(item);
            Assert.Equal(input, results.Select(x => x.Bytes.ToString()));
        }

        [Fact]
        public async Task Set_routing_key_per_message_and_consume_them_in_the_same_process()
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

            await CompleteWithin(Source.From(input)
                    .Select(s => new OutgoingMessage(ByteString.FromString(s), false, false, routingKey: GetRoutingKey(s)))
                    .RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            var result = await CompleteWithin(amqpSource
                    .Take(input.Length)
                    .RunWith(Sink.Seq<IncomingMessage>(), _mat), TimeSpan.FromSeconds(15));

            Assert.Equal(routingKeys, result.Select(x => x.Envelope.RoutingKey));
            Assert.Equal(input, result.Select(x => x.Bytes.ToString()));
        }

        [Fact]
        public async Task Publish_from_one_source_and_consume_elements_with_multiple_sinks()
        {
            var queueName = "amqp-conn-it-spec-work-queues-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);
            var amqpSink = AmqpSink.CreateSimple(
                AmqpSinkSettings.Create(_connectionSettings)
                                .WithRoutingKey(queueName)
                                .WithDeclarations(queueDeclaration));

            var input = new[] {"one", "two", "three", "four", "five"};
            var task = Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat);

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

            var result = await CompleteWithin(mergedSources.Select(x => x.Bytes.ToString()).Take(input.Length).RunWith(Sink.Seq<string>(), _mat), TimeSpan.FromSeconds(15));
            
            Assert.Equal(input.OrderBy(x => x).ToArray(), result.OrderBy(x => x).ToArray());

            await CompleteWithin(task, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task Publish_elements_with_flow_then_consume_them_with_source()
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

            var passedThrough = await CompleteWithin(Source.From(input)
                    .Select(x => (new OutgoingMessage(ByteString.FromString(x), true, true), x))
                    .Via(amqpFlow)
                    .RunWith(Sink.Seq<string>(), _mat), TimeSpan.FromSeconds(15));

            Assert.Equal(input, passedThrough, StringComparer.InvariantCulture);

            var consumed = await CompleteWithin(AmqpSource.AtMostOnceSource(
                        NamedQueueSourceSettings.Create(_connectionSettings, queueName).WithDeclarations(queueDeclaration),
                        bufferSize: 10)
                    .Select(m => m.Bytes.ToString(Encoding.UTF8))
                    .Take(input.Length)
                    .RunWith(Sink.Seq<string>(), _mat), TimeSpan.FromSeconds(15));

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

            await CompleteWithin(Source.From(input)
                    .Select(s => (new OutgoingMessage(ByteString.FromString(s), false, false, routingKey: GetRoutingKey(s)), s))
                    .Via(amqpFlow)
                    .RunWith(Sink.Ignore<string>(), _mat), TimeSpan.FromSeconds(15));

            var result = await CompleteWithin(amqpSource
                    .Take(input.Length)
                    .RunWith(Sink.Seq<IncomingMessage>(), _mat), TimeSpan.FromSeconds(15));

            Assert.Equal(routingKeys, result.Select(x => x.Envelope.RoutingKey));
            Assert.Equal(input, result.Select(x => x.Bytes.ToString()));
        }

        [Fact]
        public async Task Declare_connection_that_does_not_require_server_acks()
        {
            //var connectionSettings = AmqpConnectionDetails.Create("localhost", 5672);

            var queueName = "amqp-conn-it-spec-fire-and-forget-" + Environment.TickCount;
            var queueDeclaration = QueueDeclaration.Create(queueName);

            var amqpSink = AmqpSink.CreateSimple(AmqpSinkSettings.Create(_connectionSettings)
                .WithRoutingKey(queueName)
                .WithDeclarations(queueDeclaration));

            var amqpSource = AmqpSource.CommittableSource(NamedQueueSourceSettings.Create(_connectionSettings, queueName)
                .WithAckRequired(false)
                .WithDeclarations(queueDeclaration), bufferSize: 10);

            var input = new[] {"one", "two", "three", "four", "five"};
            await CompleteWithin(Source.From(input).Select(ByteString.FromString).RunWith(amqpSink, _mat), TimeSpan.FromSeconds(15));

            var result = await CompleteWithin(amqpSource.Take(input.Length).RunWith(Sink.Seq<CommittableIncomingMessage>(), _mat), TimeSpan.FromSeconds(15));
            Assert.Equal(input, result.Select(x => x.Message.Bytes.ToString(Encoding.UTF8)));
        }

        [Fact]
        public async Task Durable_queues_interoperate_on_stock_broker_and_cancelled_deliveries_can_be_nacked_before_shutdown()
        {
            await using var broker = new RabbitMqBuilder("rabbitmq:4.4.0").Build();
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await broker.StartAsync(startup.Token);
            var factory = new ConnectionFactory { Uri = new Uri(broker.GetConnectionString()) };
            using (var legacyConnection = factory.CreateConnection())
            using (var legacy = legacyConnection.CreateModel())
            {
                var failure = Assert.Throws<OperationInterruptedException>(() =>
                    legacy.QueueDeclare("rejected-legacy-default", durable: false, exclusive: false, autoDelete: false));
                Assert.Contains("transient_nonexcl_queues", failure.ShutdownReason.ReplyText);
            }

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();
            const string queue = "durable-native-interop";
            var settings = AmqpConnectionUri.Create(new Uri(broker.GetConnectionString()));
            var declaration = QueueDeclaration.Create(queue).WithDurable(true);
            var sink = AmqpSink.Create(AmqpSinkSettings.Create(settings)
                .WithRoutingKey(queue).WithDeclarations(declaration).WithWaitForConfirms(TimeSpan.FromSeconds(5)));
            var expected = new[] { "first", "café", "last" };
            await CompleteWithin(Source.From(expected).Select(payload =>
                {
                    var properties = channel.CreateBasicProperties();
                    properties.ContentType = "text/plain;charset=utf-8";
                    properties.MessageId = payload;
                    properties.Persistent = true;
                    return new OutgoingMessage(ByteString.FromString(payload), false, false, properties);
                }).RunWith(sink, _mat), TimeSpan.FromSeconds(15));
            foreach (var payload in expected)
            {
                var received = channel.BasicGet(queue, autoAck: false);
                Assert.NotNull(received);
                Assert.Equal(payload, Encoding.UTF8.GetString(received.Body.ToArray()));
                Assert.Equal("text/plain;charset=utf-8", received.BasicProperties.ContentType);
                Assert.Equal(payload, received.BasicProperties.MessageId);
                Assert.True(received.BasicProperties.Persistent);
                channel.BasicAck(received.DeliveryTag, multiple: false);
            }
            Assert.Null(channel.BasicGet(queue, autoAck: true));

            channel.BasicPublish("", queue, basicProperties: null, Encoding.UTF8.GetBytes("cancel-me"));
            var probe = AmqpSource.CommittableSource(NamedQueueSourceSettings.Create(settings, queue), 1)
                .RunWith(this.SinkProbe<CommittableIncomingMessage>(), _mat);
            probe.EnsureSubscription();
            try
            {
                var delivered = probe.Request(1).ExpectNext();
                Assert.Equal("cancel-me", delivered.Message.Bytes.ToString(Encoding.UTF8));
                Assert.False(delivered.Message.Envelope.Redeliver);
                probe.Cancel();
                // The existing source contract retains its connection until emitted
                // deliveries have been explicitly acknowledged or rejected.
                await CompleteWithin(delivered.Nack(requeue: true), TimeSpan.FromSeconds(5));
                BasicGetResult? redelivered = null;
                await AwaitAssertAsync(() =>
                {
                    Assert.Equal(0u, channel.QueueDeclarePassive(queue).ConsumerCount);
                    redelivered = channel.BasicGet(queue, autoAck: false);
                    Assert.NotNull(redelivered);
                });
                Assert.True(redelivered!.Redelivered);
                Assert.Equal("cancel-me", Encoding.UTF8.GetString(redelivered.Body.ToArray()));
                channel.BasicAck(redelivered.DeliveryTag, multiple: false);
                Assert.Null(channel.BasicGet(queue, autoAck: true));
                Assert.True(connection.IsOpen);
            }
            finally { probe.Cancel(); }
        }
    }
}
