using System.Threading.Tasks;
using Akka.Serialization;
using Amqp;
using Amqp.Framing;
using Amqp.Types;

namespace Akka.Streams.Amqp.V1
{
    public class NamedQueueSourceSettings<T> : IAmqpSourceSettings<T>
    {
        private readonly Session _session;
        private readonly string _linkName;
        private readonly string _queueName;
        private readonly Serializer _serializer;

        public bool ManageConnection => false;

        public bool IsClosed => _session?.IsClosed ?? true;

        public NamedQueueSourceSettings(
            Session session,
            string linkName,
            string queueName,
            int credit,
            Serializer serializer)
        {
            _session = session;
            _linkName = linkName;
            _queueName = queueName;
            Credit = credit;
            _serializer = serializer;
        }

        public T Convert(Message message)
        {
            var bString = message.GetBody<byte[]>();
            return _serializer.FromBinary<T>(bString);
        }

        public void CloseConnection()
        {
            _session.Close();
            _session.Connection.Close();
        }

        public async Task CloseConnectionAsync()
        {
            await _session.CloseAsync();
            await _session.Connection.CloseAsync();
        }

        public int Credit { get; }
        public ReceiverLink GetReceiverLink() => new ReceiverLink(_session, _linkName, new Source
        {
            Address = _queueName,
            Capabilities = new[] { new Symbol("queue") }
        }, null);
    }
}
