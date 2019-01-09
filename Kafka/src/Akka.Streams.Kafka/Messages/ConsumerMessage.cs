using Confluent.Kafka;

namespace Akka.Streams.Kafka.Messages
{
    public struct ConsumerMessage<K, V> : IConsumerMessage
    {
        public ConsumerMessage(MessageType messageType, ConsumeResult<K, V> record)
        {
            MessageType = messageType;
            Record = record;
        }

        public MessageType MessageType { get; }
        public ConsumeResult<K, V> Record { get; }

        public string Topic => Record.Topic;

        public object Value => Record.Value;
    }
}
