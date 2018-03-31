using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Kafka.Stages;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Dsl
{
    public static class Producer
    {
        public static Sink<MessageAndMeta<TKey, TValue>, Task> PlainSink<TKey, TValue>(ProducerSettings<TKey, TValue> settings)
        {
            return Flow
                .Create<MessageAndMeta<TKey, TValue>>()
                .Via(CreateFlow(settings))
                .ToMaterialized(Sink.Ignore<DeliveryReport<TKey, TValue>>(), Keep.Right);
        }

        // TODO: work on naming
        public static Flow<MessageAndMeta<TKey, TValue>, DeliveryReport<TKey, TValue>, NotUsed> CreateFlow<TKey, TValue>(ProducerSettings<TKey, TValue> settings)
        {
            var flow = Flow.FromGraph(new ProducerStage<TKey, TValue>(settings))
                .SelectAsync(settings.Parallelism, x => x);

            return string.IsNullOrEmpty(settings.DispatcherId) 
                ? flow
                : flow.WithAttributes(ActorAttributes.CreateDispatcher(settings.DispatcherId));
        }
    }
}
