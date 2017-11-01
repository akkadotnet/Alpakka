using Akka.IO;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public sealed class OutgoingMessage
    {
        public static OutgoingMessage Create(ByteString bytes, bool immediate, bool mandatory,
            IBasicProperties properties = null) => new OutgoingMessage(bytes, immediate, mandatory, properties);
        private OutgoingMessage(ByteString bytes, bool immediate, bool mandatory, IBasicProperties properties = null)
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
