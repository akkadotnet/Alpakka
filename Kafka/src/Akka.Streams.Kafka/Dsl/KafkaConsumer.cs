using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Kafka.Stages;
using Confluent.Kafka;
using Akka.Streams.Kafka.Messages;

namespace Akka.Streams.Kafka.Dsl
{
    /// <summary>
    /// Akka Stream connector for subscribing to Kafka topics.
    /// </summary>
    public static class KafkaConsumer
    {
        /// <summary>
        /// The <see cref="PlainSource{K,V}"/> emits <see cref="ConsumerRecord"/> elements (as received from the underlying 
        /// <see cref="IConsumer{TKey,TValue}"/>). It has no support for committing offsets to Kafka. It can be used when the
        /// offset is stored externally or with auto-commit (note that auto-commit is by default disabled).
        /// The consumer application doesn't need to use Kafka's built-in offset storage and can store offsets in a store of its own
        /// choosing. The primary use case for this is allowing the application to store both the offset and the results of the
        /// consumption in the same system in a way that both the results and offsets are stored atomically.This is not always
        /// possible, but when it is, it will make the consumption fully atomic and give "exactly once" semantics that are
        /// stronger than the "at-least once" semantics you get with Kafka's offset commit functionality.
        /// </summary>
        public static Source<ConsumerMessage<K, V>, Task> PlainSource<K, V>(ConsumerSettings<K, V> settings, ISubscription subscription)
        {
            return Source.FromGraph(new KafkaSourceStage<K, V>(settings, subscription));
        }

        /// <summary>
        /// The <see cref="CommittableSource{K,V}"/> makes it possible to commit offset positions to Kafka.
        /// This is useful when "at-least once delivery" is desired, as each message will likely be
        /// delivered one time but in failure cases could be duplicated.
        /// Compared to auto-commit, this gives exact control over when a message is considered consumed.
        /// If you need to store offsets in anything other than Kafka, <see cref="PlainSource{K,V}"/> should
        /// be used instead of this API.
        /// </summary>
        public static Source<CommittableMessage<K, V>, Task> CommittableSource<K, V>(ConsumerSettings<K, V> settings, ISubscription subscription)
        {
            return Source.FromGraph(new CommittableConsumerStage<K, V>(settings, subscription));
        }
    }
}
