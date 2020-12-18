using Akka.IO;
using Amqp;
using Akka.Serialization;
using Amqp.Framing;
using Amqp.Types;

namespace Akka.Streams.Amqp.V1
{
    public class NamedQueueSinkSettings<T> : IAmqpSinkSettings<T>
    {
        private readonly Session _session;
        private readonly string _linkName;
        private readonly string _queueName;
        private readonly Serializer _serializer;

        public bool ManageConnection => false;

        public NamedQueueSinkSettings(
            Session session,
            string linkName,
            string queueName,
            Serializer serializer)
        {
            _session = session;
            _linkName = linkName;
            _queueName = queueName;
            _serializer = serializer;
        }

        public byte[] GetBytes(T obj)
        {
            return _serializer.ToBinary(obj);
        }

        public SenderLink GetSenderLink() => new SenderLink(_session, _linkName, new Target
        {
            Address = _queueName,
            Capabilities = new[] { new Symbol("queue") }
        }, null);
    }
}
