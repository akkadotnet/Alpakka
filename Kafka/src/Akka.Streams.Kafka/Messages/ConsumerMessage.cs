using Confluent.Kafka;

namespace Akka.Streams.Kafka.Messages
{
    public struct ConsumerMessage<K, V>
    {
        public ConsumerMessage(MessageType messageType, ConsumerRecord<K, V> record)
        {
            MessageType = messageType;
            Record = record;            
        }
               
        public MessageType MessageType { get; }
        public ConsumerRecord<K, V> Record { get; }
    }
}
