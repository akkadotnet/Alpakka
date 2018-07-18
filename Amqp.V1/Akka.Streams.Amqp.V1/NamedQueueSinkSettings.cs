using Akka.IO;
using Amqp;
using Akka.Serialization;
using Amqp.Framing;
using Amqp.Types;

namespace Akka.Streams.Amqp.V1
{
    public class NamedQueueSinkSettings<T> : IAmpqSinkSettings<T>
    {
        private readonly Session session;
        private readonly string linkName;
        private readonly string queueName;
        private readonly Serializer serializer;

        public NamedQueueSinkSettings(
            Session session,
            string linkName,
            string queueName,
            Serializer serializer)
        {
            this.session = session;
            this.linkName = linkName;
            this.queueName = queueName;
            this.serializer = serializer;
        }

        public byte[] GetBytes(T obj)
        {
            return serializer.ToBinary(obj);
        }

        public SenderLink GetSenderLink() => new SenderLink(session, linkName, new Target
        {
            Address = queueName,
            Capabilities = new Symbol[] { new Symbol("queue") }
        }, null);
    }
}
