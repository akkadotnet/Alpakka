using Akka.IO;
using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public interface IAmpqSinkSettings<T>
    {
        SenderLink GetSenderLink();
        byte[] GetBytes(T obj);
    }
}
