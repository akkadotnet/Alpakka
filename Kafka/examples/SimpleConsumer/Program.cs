using System;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;
using Akka.Streams.Kafka.Dsl;
using Akka.Streams.Kafka.Settings;
using Confluent.Kafka;
using Confluent.Kafka.Serialization;

namespace SimpleConsumer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Config fallbackConfig = ConfigurationFactory.ParseString(@"
                    akka.suppress-json-serializer-warning=true
                    akka.loglevel = DEBUG
                ").WithFallback(ConfigurationFactory.FromResource<ConsumerSettings<object, object>>("Akka.Streams.Kafka.reference.conf"));

            var system = ActorSystem.Create("TestKafka", fallbackConfig);
            var materializer = system.Materializer();

            var consumerSettings = ConsumerSettings<Null, string>.Create(system, null, new StringDeserializer(Encoding.UTF8))
                .WithBootstrapServers("localhost:29092")
                .WithGroupId("group1");

            var subscription = Subscriptions.Topics("akka100");

            KafkaConsumer.PlainSource(consumerSettings, subscription)
                .RunForeach(result =>
                {
                    Console.WriteLine($"Consumer: {result.Record.Topic}/{result.Record.Partition} {result.Record.Offset}: {result.Record.Value}");
                }, materializer);


            Console.ReadLine();
        }
    }
}
