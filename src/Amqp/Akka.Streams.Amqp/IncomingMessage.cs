using Akka.IO;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    public sealed class IncomingMessage
    {
        public IncomingMessage(ByteString bytes, Envelope envelope, IBasicProperties properties)
        {
            Bytes = bytes;
            Envelope = envelope;
            Properties = properties;
        }
        
        public ByteString Bytes { get; }
        public Envelope Envelope { get; }
        public IBasicProperties Properties { get; }
        public override string ToString() => $"IncomingMessage(Bytes={Bytes}, Envelope={Envelope}, Properties={Properties})";
    }
}
