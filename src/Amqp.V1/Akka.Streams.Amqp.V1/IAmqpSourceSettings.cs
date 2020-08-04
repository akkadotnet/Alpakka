using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public interface IAmqpSourceSettings<T>
    {
        ReceiverLink GetReceiverLink();
        int Credit { get; }
        T Convert(Message message);
    }
}
