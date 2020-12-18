using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Serialization;
using Amqp;
using Amqp.Framing;
using Amqp.Types;

namespace Akka.Streams.Amqp.V1
{
    public class AddressSourceSettings<T> : IAmqpSourceSettings<T>
    {
        private readonly string _linkName;
        private readonly string _queueName;
        private readonly Serializer _serializer;

        public int Credit { get; }
        private readonly Address _address;
        private Connection _connection;
        private Session _session;

        public bool ManageConnection => true;

        public AddressSourceSettings(
            Address address,
            string linkName,
            string queueName,
            int credit,
            Serializer serializer)
        {
            _address = address;
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

        public ReceiverLink GetReceiverLink() 
        {
            if(_connection == null || _connection.IsClosed)
                _connection = new Connection(_address);

            if (_session == null || _session.IsClosed)
                _session = new Session(_connection);

            return new ReceiverLink(
                _session, 
                _linkName,
                new Source
                {
                    Address = _queueName, 
                    Capabilities = new[] { new Symbol("queue") }
                }, 
                null);
        }
    }
}
