using System;

namespace Akka.Streams.SignalR.Internals
{
    public interface IBuffer<T>
    {
        int Capacity { get; }
        bool TryEnqueue(T msg);
        bool TryDequeue(out T msg);
    }

    public sealed class FixedSizeBuffer<T> : IBuffer<T>
    {
        private readonly T[] buffer;
        private int readIdx = 0;
        private int writeIdx = 0;

        public int Capacity { get; }

        public FixedSizeBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Buffer capacity must be positive number");

            Capacity = capacity + 1;
            buffer = new T[Capacity];
        }

        public bool TryEnqueue(T msg)
        {
            var w = (writeIdx + 1) % Capacity;
            if (w == readIdx)
                return false;
            
            writeIdx = w;
            buffer[writeIdx] = msg;
            return true;
        }

        public bool TryDequeue(out T msg)
        {
            if (readIdx == writeIdx)
            {
                msg = default(T);
                return false;
            }

            readIdx = (readIdx + 1) % Capacity;
            msg = buffer[readIdx];
            return true;
        }
    }
}