using System;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.Dsl
{
    public sealed class CommittableIncomingMessage
    {
        private readonly Func<bool, TaskCompletionSource<Done>> _ack;
        private readonly Func<bool, bool, TaskCompletionSource<Done>> _nack;
        public IncomingMessage Message { get; }

        public CommittableIncomingMessage(IncomingMessage message, Func<bool, TaskCompletionSource<Done>> ack, 
            Func<bool, bool, TaskCompletionSource<Done>> nack)
        {
            _ack = ack;
            _nack = nack;
            Message = message;
        }

        public TaskCompletionSource<Done> Ack(bool multiple = false) => _ack(multiple);
        public TaskCompletionSource<Done> Nack(bool multiple = false, bool requeue = true) => _nack(multiple, requeue);
    }
}