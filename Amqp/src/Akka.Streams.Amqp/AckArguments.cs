using System.Threading.Tasks;

namespace Akka.Streams.Amqp
{
    public sealed class AckArguments : CommitCallback
    {
        public AckArguments(ulong deliveryTag, bool multiple, TaskCompletionSource<Done> promise)
            : base(deliveryTag, multiple, promise)
        {
        }
    }
}