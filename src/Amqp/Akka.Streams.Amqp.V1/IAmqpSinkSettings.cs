using Akka.IO;
using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public interface IAmqpSinkSettings<in T>
    {
        SenderLink GetSenderLink();
        byte[] GetBytes(T obj);
    }
}
