using Akka.Serialization;
using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public class NamedQueueSourceSettings<T> : IAmqpSourceSettings<T>
    {
        private readonly Session session;
        private readonly string linkName;
        private readonly string queueName;
        private readonly Serializer serializer;

        public NamedQueueSourceSettings(
            Session session,
            string linkName,
            string queueName,
            int credit,
            Serializer serializer)
        {
            this.session = session;
            this.linkName = linkName;
            this.queueName = queueName;
            Credit = credit;
            this.serializer = serializer;
        }

        public T Convert(Message message) {
            var bString = message.GetBody<byte[]>();
            return serializer.FromBinary<T>(bString);
        }

        public int Credit { get; }
        public ReceiverLink GetReceiverLink() => new ReceiverLink(session, linkName, queueName);
    }
}
