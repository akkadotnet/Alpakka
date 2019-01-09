namespace Akka.Streams.Kafka.Messages
{
    public interface IConsumerMessage
    {
        string Topic { get; }
        object Value { get; }
    }
}
