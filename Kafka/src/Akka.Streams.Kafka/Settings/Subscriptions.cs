using System.Collections.Immutable;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Settings
{
    public interface ISubscription { }
    public interface IManualSubscription : ISubscription { }
    public interface IAutoSubscription : ISubscription { }

    internal sealed class TopicSubscription : IAutoSubscription
    {
        public TopicSubscription(IImmutableSet<string> topics)
        {
            Topics = topics;
        }

        public IImmutableSet<string> Topics { get; }
    }

    internal sealed class Assignment : IManualSubscription
    {
        public Assignment(IImmutableSet<TopicPartition> topicPartitions)
        {
            TopicPartitions = topicPartitions;
        }

        public IImmutableSet<TopicPartition> TopicPartitions { get; }
    }

    internal sealed class AssignmentWithOffset : IManualSubscription
    {
        public AssignmentWithOffset(IImmutableSet<TopicPartitionOffset> topicPartitions)
        {
            TopicPartitions = topicPartitions;
        }

        public IImmutableSet<TopicPartitionOffset> TopicPartitions { get; }
    }

    public static class Subscriptions
    {
        public static IAutoSubscription Topics(params string[] topics) =>
            new TopicSubscription(topics.ToImmutableHashSet());

        public static IManualSubscription Assignment(params TopicPartition[] topicPartitions) =>
            new Assignment(topicPartitions.ToImmutableHashSet());

        public static IManualSubscription AssignmentWithOffset(params TopicPartitionOffset[] topicPartitions) =>
            new AssignmentWithOffset(topicPartitions.ToImmutableHashSet());
    }
}