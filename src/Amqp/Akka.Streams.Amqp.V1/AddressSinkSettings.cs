using System;
using System.Collections.Generic;
using System.Text;
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

        private Connection _connection;
        private Session _session;

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
