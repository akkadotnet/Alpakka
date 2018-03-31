using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.Kafka.Dsl;
using Akka.Streams.Kafka.Settings;
using Confluent.Kafka;
using Confluent.Kafka.Serialization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Kafka.Tests.Integration
{
    public class PlainSinkIntegrationTests : Akka.TestKit.Xunit2.TestKit
    {
        private const string KafkaUrl = "localhost:29092";
        private const string InitialMsg = "initial msg in topic, required to create the topic before any consumer subscribes to it";
        private readonly ActorMaterializer _materializer;

        private string Uuid { get; } = Guid.NewGuid().ToString();

        private string CreateTopic(int number) => $"topic-{number}-{Uuid}";
        private string CreateGroup(int number) => $"group-{number}-{Uuid}";

        public PlainSinkIntegrationTests(ITestOutputHelper output) 
            : base(ConfigurationFactory.FromResource<ConsumerSettings<object, object>>("Akka.Streams.Kafka.reference.conf"), null, output)
        {
            _materializer = Sys.Materializer();
        }

        private async Task GivenInitializedTopic(string topic)
        {
            var producer = ProducerSettings.CreateKafkaProducer();
            await producer.ProduceAsync(topic, new Message<Null, string> {Value = InitialMsg});
            producer.Dispose();
        }

        private ProducerSettings<Null, string> ProducerSettings =>
            ProducerSettings<Null, string>.Create(Sys, null, new StringSerializer(Encoding.UTF8))
                .WithBootstrapServers(KafkaUrl);

        private ConsumerSettings<Null, string> CreateConsumerSettings(string group)
        {
            return ConsumerSettings<Null, string>.Create(Sys, null, new StringDeserializer(Encoding.UTF8))
                .WithBootstrapServers(KafkaUrl)
                .WithProperty("auto.offset.reset", "earliest")
                .WithGroupId(group);
        }

        [Fact]
        public async Task PlainSink_should_publish_100_elements_to_Kafka_producer()
        {
            var topic1 = CreateTopic(1);
            var group1 = CreateGroup(1);

            await GivenInitializedTopic(topic1);

            var consumerSettings = CreateConsumerSettings(group1);
            var consumer = consumerSettings.CreateKafkaConsumer();
            consumer.Assign(new List<TopicPartition> { new TopicPartition(topic1, 0) });

            var task = new TaskCompletionSource<NotUsed>();
            int messagesReceived = 0;

            consumer.OnRecord += (sender, message) =>
            {
                messagesReceived++;
                if (messagesReceived == 100)
                    task.SetResult(NotUsed.Instance);
            };

            await Source
                .From(Enumerable.Range(1, 100))
                .Select(c => c.ToString())
                .Select(elem => new MessageAndMeta<Null, string> { Topic = topic1, Message = new Message<Null, string> { Value = elem } })
                .RunWith(KafkaProducer.PlainSink(ProducerSettings), _materializer);

            var dateTimeStart = DateTime.UtcNow;

            bool CheckTimeout(TimeSpan timeout)
            {
                return dateTimeStart.AddSeconds(timeout.TotalSeconds) > DateTime.UtcNow;
            }

            while (!task.Task.IsCompleted && CheckTimeout(TimeSpan.FromMinutes(1)))
            {
                consumer.Poll(TimeSpan.FromSeconds(1));
            }

            messagesReceived.Should().Be(100);
        }

        [Fact]
        public async Task PlainSink_should_fail_stage_if_broker_unavailable()
        {
            var topic1 = CreateTopic(1);

            await GivenInitializedTopic(topic1);

            var config = ProducerSettings<Null, string>.Create(Sys, null, new StringSerializer(Encoding.UTF8))
                .WithBootstrapServers("localhost:10092");

            Action act = () => Source
                .From(Enumerable.Range(1, 100))
                .Select(c => c.ToString())
                .Select(elem => new MessageAndMeta<Null, string> { Topic = topic1, Message = new Message<Null, string> { Value = elem } })
                .RunWith(KafkaProducer.PlainSink(config), _materializer).Wait();

            // TODO: find a better way to test FailStage
            act.Should().Throw<AggregateException>().WithInnerException<KafkaException>();
        }
    }
}
