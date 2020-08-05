using Akka.IO;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public sealed class OutgoingMessage
    {
        public OutgoingMessage(ByteString bytes, bool immediate, bool mandatory, IBasicProperties properties = null, string routingKey = null)
        {
            Bytes = bytes;
            Immediate = immediate;
            Mandatory = mandatory;
            Properties = properties;
            RoutingKey = routingKey;
        }

        public ByteString Bytes { get; }
        public bool Immediate { get; }
        public bool Mandatory { get; }
        public IBasicProperties Properties { get; }
        public string RoutingKey { get; }
    }
}