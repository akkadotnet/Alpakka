using System.Collections.Generic;

namespace Akka.Streams.Amqp.V1.Util
{
    internal static class QueueExtensions
    {
        public static bool TryDequeue<T>(this Queue<T> queue, out T result)
        {
            if (queue.Count == 0)
            {
                result = default;
                return false;
            }

            result = queue.Dequeue();
            return true;
        }
    }
}
