using System.Threading.Tasks;

namespace Akka.Streams.Amqp
{
    public sealed class NackArguments : ICommitCallback
    {
        public ulong DeliveryTag { get; }
        public bool Multiple { get; }
        public bool Requeue { get; }
        public TaskCompletionSource<Done> Promise { get; }

        public NackArguments(ulong deliveryTag, bool multiple, bool requeue, TaskCompletionSource<Done> promise)
        {
            DeliveryTag = deliveryTag;
            Multiple = multiple;
            Requeue = requeue;
            Promise = promise;
        }
    }
}