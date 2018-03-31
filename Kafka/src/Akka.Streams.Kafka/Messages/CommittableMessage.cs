using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Messages
{
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
