namespace System.Collections.Generic
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
