using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Settings
{
    public interface ISubscription
    {
        void AssignConsumer<K, V>(IConsumer<K, V> consumer);
        IEnumerable<TopicPartition> GetTopicPartitions();
    }

    public abstract class Subscription : ISubscription
    {
        public abstract void AssignConsumer<K, V>(IConsumer<K, V> consumer);
        public abstract IEnumerable<TopicPartition> GetTopicPartitions();
    }

    internal sealed class TopicSubscription : Subscription
    {
        public TopicSubscription(IImmutableSet<string> topics, Dictionary<string, string> kafkaConfiguration = null)
        {
            Topics = topics;
            this.kafkaConfiguration = kafkaConfiguration;
        }

        public IImmutableSet<string> Topics { get; }

        private Dictionary<string, string> kafkaConfiguration;

        public override void AssignConsumer<K, V>(IConsumer<K, V> consumer)
        {
            consumer.Subscribe(Topics);
        }

        public override IEnumerable<TopicPartition> GetTopicPartitions()
        {
            if (kafkaConfiguration != null)
            {
                var adminClient = new AdminClient(kafkaConfiguration);
                Metadata metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(3));
                if (metadata != null)
                {
                    var d = metadata.Topics.ToDictionary(x => x.Topic);
                    foreach (var topic in Topics.Where(t => d.ContainsKey(t)))
                    {
                        var dt = d[topic];
                        foreach (var partition in dt.Partitions)
                        {
                            yield return new TopicPartition(topic, partition.PartitionId);
                        }
                    }
                }
            }
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

        public override IEnumerable<TopicPartition> GetTopicPartitions()
        {
            return TopicPartitions;
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

        public override IEnumerable<TopicPartition> GetTopicPartitions()
        {
            return TopicPartitions.Select(x => x.TopicPartition);
        }
    }

    public static class Subscriptions
    {
        public static ISubscription Topics(params string[] topics) =>
            new TopicSubscription(topics.ToImmutableHashSet());

        public static ISubscription Topics(Dictionary<string, string> kafkaConfiguration, params string[] topics) =>
            new TopicSubscription(topics.ToImmutableHashSet(), kafkaConfiguration);

        public static ISubscription Assignment(params TopicPartition[] topicPartitions) =>
            new Assignment(topicPartitions.ToImmutableHashSet());

        public static ISubscription AssignmentWithOffset(params TopicPartitionOffset[] topicPartitions) =>
            new AssignmentWithOffset(topicPartitions.ToImmutableHashSet());
    }
}