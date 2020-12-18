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
    public class AddressSinkSettings<T> : IAmqpSinkSettings<T>
    {
        private readonly string _linkName;
        private readonly string _queueName;
        private readonly Serializer _serializer;
        private readonly Address _address;
        private readonly object _lock = new object();

        private Connection _connection;
        private Session _session;

        public bool ManageConnection => true;

        public AddressSinkSettings(
            Address address,
            string linkName,
            string queueName,
            Serializer serializer)
        {
            _address = address;
            _linkName = linkName;
            _queueName = queueName;
            _serializer = serializer;
        }

        public byte[] GetBytes(T obj)
        {
            return _serializer.ToBinary(obj);
        }

        public void CloseConnection()
        {
            _session?.Close();
            _connection?.Close();

            _session = null;
            _connection = null;
        }

        public async Task CloseConnectionAsync()
        {
            if(_session != null)
                await _session.CloseAsync();

            if(_connection != null)
                await _connection.CloseAsync();
        }

        public SenderLink GetSenderLink()
        {
            if (_connection == null || _connection.IsClosed)
                _connection = new Connection(_address);

            if (_session == null || _session.IsClosed)
                _session = new Session(_connection);

            return new SenderLink(
                _session, 
                _linkName,
                new Target
                {
                    Address = _queueName, 
                    Capabilities = new[] { new Symbol("queue") }
                }, 
                null);
        }
    }
}
