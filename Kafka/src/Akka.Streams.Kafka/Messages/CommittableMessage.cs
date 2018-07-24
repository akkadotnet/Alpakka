using System;
using Akka.Streams.Kafka.Dsl;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Messages
{
    /// <summary>
    /// Output element of <see cref="KafkaConsumer.CommittableSource{K,V}"/>.
    /// The offset can be committed via the included <see cref="CommitableOffset"/>.
    /// </summary>
    public sealed class CommittableMessage<K, V>
    {
        public CommittableMessage(ConsumerRecord<K, V> record, CommitableOffset commitableOffset)
        {
            Record = record;
            CommitableOffset = commitableOffset;
        }

        public ConsumerRecord<K, V> Record { get; }

        public CommitableOffset CommitableOffset { get; }
    }

    /// <summary>
    /// Included in <see cref="CommittableMessage{K,V}"/>. Makes it possible to
    /// commit an offset or aggregate several offsets before committing.
    /// Note that the offset position that is committed to Kafka will automatically
    /// be one more than the `offset` of the message, because the committed offset
    /// should be the next message your application will consume,
    /// i.e. lastProcessedMessageOffset + 1.
    /// </summary>
    public class CommitableOffset
    {
        private readonly Func<CommittedOffsets> _task;

        public CommitableOffset(Func<CommittedOffsets> task, PartitionOffset offset)
        {
            _task = task;
            Offset = offset;
        }

        public PartitionOffset Offset { get; }

        public CommittedOffsets Commit()
        {
            return _task();
        }
    }

    /// <summary>
    /// Offset position for a groupId, topic, partition.
    /// </summary>
    public class PartitionOffset
    {
        public PartitionOffset(string groupId, string topic, int partition, Offset offset)
        {
            GroupId = groupId;
            Topic = topic;
            Partition = partition;
            Offset = offset;
        }

        public string GroupId { get; }

        public string Topic { get; }

        public int Partition { get; }

        public Offset Offset { get; }
    }
}
