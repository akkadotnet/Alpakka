using System;
using System.Collections.Generic;
using System.Text;
using Amqp;
using Amqp.Framing;

namespace Akka.Streams.Amqp.V1
{
    public class DisconnectedException : Exception
    {
        public IAmqpObject Sender { get; }
        public Error Error { get; }

        public DisconnectedException(IAmqpObject sender, Error error) : base(error.Description)
        {
            Sender = sender;
            Error = error;
        }
    }
}
