using System;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.Dsl
{
    public sealed class CommittableIncomingMessage
    {
        private readonly Func<bool, Task> _ack;
        private readonly Func<bool, bool, Task> _nack;
        public IncomingMessage Message { get; }

        public CommittableIncomingMessage(IncomingMessage message, Func<bool, Task> ack, Func<bool, bool, Task> nack)
        {
            _ack = ack;
            _nack = nack;
            Message = message;
        }

        public Task Ack(bool multiple = false) => _ack(multiple);
        public Task Nack(bool multiple = false, bool requeue = true) => _nack(multiple, requeue);
    }
}