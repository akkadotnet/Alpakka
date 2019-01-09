using Confluent.Kafka;

namespace Akka.Streams.Kafka.Messages
{
    public struct ProduceMessage<Tkey, TValue>
    {
        public ProduceMessage(string topic, Message<Tkey,TValue> message)
        {
            Topic = topic;
            Message = message;
        }

        public string Topic { get; }
        public Message<Tkey,TValue> Message { get; }
    }
}
