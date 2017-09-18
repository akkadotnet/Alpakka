using Akka.IO;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public class OutgoingMessage
    {
        public OutgoingMessage(ByteString bytes, bool immediate, bool mandatory, IBasicProperties properties = null)
        {
            Bytes = bytes;
            Immediate = immediate;
            Mandatory = mandatory;
            Properties = properties;
        }

        public ByteString Bytes { get; }

        public bool Immediate { get; }

        public bool Mandatory { get; }

        public IBasicProperties Properties { get; }
    }
}
