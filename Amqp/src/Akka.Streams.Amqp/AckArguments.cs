using System.Threading.Tasks;

namespace Akka.Streams.Amqp
{
    public sealed class AckArguments : ICommitCallback
    {
        public ulong DeliveryTag { get; }
        public bool Multiple { get; }
        public TaskCompletionSource<Done> Promise { get; }

        public AckArguments(ulong deliveryTag, bool multiple, TaskCompletionSource<Done> promise)
        {
            Multiple = multiple;
            Promise = promise;
            DeliveryTag = deliveryTag;
        }
    }
}