using System;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp
{
    public abstract class CommitCallback
    {
        private readonly TaskCompletionSource<Done> _promise;
        public ulong DeliveryTag { get; }
        public bool Multiple { get; }

        protected CommitCallback(ulong deliveryTag, bool multiple, TaskCompletionSource<Done> promise)
        {
            DeliveryTag = deliveryTag;
            Multiple = multiple;
            _promise = promise;
        }

        public void Commit() => _promise.TrySetResult(Done.Instance);
        public void Fail(Exception ex) => _promise.TrySetException(ex);
    }
}