using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Kafka.Stages;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Dsl
{
    public static class KafkaProducer
    {
        public static Sink<MessageAndMeta<TKey, TValue>, Task> PlainSink<TKey, TValue>(ProducerSettings<TKey, TValue> settings)
        {
            return Flow
                .Create<MessageAndMeta<TKey, TValue>>()
                .Via(PlainFlow(settings))
                .ToMaterialized(Sink.Ignore<DeliveryReport<TKey, TValue>>(), Keep.Right);
        }

        public static Flow<MessageAndMeta<TKey, TValue>, DeliveryReport<TKey, TValue>, NotUsed> PlainFlow<TKey, TValue>(ProducerSettings<TKey, TValue> settings)
        {
            var flow = Flow.FromGraph(new ProducerStage<TKey, TValue>(settings))
                .SelectAsync(settings.Parallelism, x => x);

            return string.IsNullOrEmpty(settings.DispatcherId) 
                ? flow
                : flow.WithAttributes(ActorAttributes.CreateDispatcher(settings.DispatcherId));
        }
    }
}
