using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public interface IAmqpSourceSettings<out T>
    {
        ReceiverLink GetReceiverLink();
        int Credit { get; }
        T Convert(Message message);
    }
}
