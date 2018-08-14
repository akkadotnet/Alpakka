using System.Collections.Immutable;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Settings
{
    public interface ISubscription {
        void AssignConsumer<K, V>(IConsumer<K, V> consumer);
    }

    public abstract class Subscription : ISubscription
    {
        public abstract void AssignConsumer<K, V>(IConsumer<K, V> consumer);
    }

    internal sealed class TopicSubscription : Subscription
    {
        public TopicSubscription(IImmutableSet<string> topics)
        {
            Topics = topics;
        }

        public IImmutableSet<string> Topics { get; }
        
        public override void AssignConsumer<K, V>(IConsumer<K, V> consumer)
        {
            consumer.Subscribe(Topics);
        }
    }

    internal sealed class Assignment : Subscription
    {
        public Assignment(IImmutableSet<TopicPartition> topicPartitions)
        {
            TopicPartitions = topicPartitions;
        }

        public IImmutableSet<TopicPartition> TopicPartitions { get; }

        public override void AssignConsumer<K, V>(IConsumer<K, V> consumer)
        {
            consumer.Assign(TopicPartitions);
        }
    }

    internal sealed class AssignmentWithOffset : Subscription
    {
        public AssignmentWithOffset(IImmutableSet<TopicPartitionOffset> topicPartitions)
        {
            TopicPartitions = topicPartitions;
        }

        public IImmutableSet<TopicPartitionOffset> TopicPartitions { get; }

        public override void AssignConsumer<K, V>(IConsumer<K, V> consumer)
        {
            consumer.Assign(TopicPartitions);
        }
    }

    public static class Subscriptions
    {
        public static ISubscription Topics(params string[] topics) =>
            new TopicSubscription(topics.ToImmutableHashSet());

        public static ISubscription Assignment(params TopicPartition[] topicPartitions) =>
            new Assignment(topicPartitions.ToImmutableHashSet());

        public static ISubscription AssignmentWithOffset(params TopicPartitionOffset[] topicPartitions) =>
            new AssignmentWithOffset(topicPartitions.ToImmutableHashSet());
    }
}