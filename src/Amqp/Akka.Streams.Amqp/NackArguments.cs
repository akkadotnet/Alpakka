using System.Threading.Tasks;

namespace Akka.Streams.Amqp
{
    public sealed class NackArguments : CommitCallback
    {
        public bool Requeue { get; }

        public NackArguments(ulong deliveryTag, bool multiple, bool requeue, TaskCompletionSource<Done> promise)
            : base(deliveryTag, multiple, promise)
        {
            Requeue = requeue;
        }
    }
}