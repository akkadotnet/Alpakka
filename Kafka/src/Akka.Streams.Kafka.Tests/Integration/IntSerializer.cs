using System;

namespace Akka.Streams.Kafka.Tests.Integration
{
    public static class IntSerializer
    {
        public static int Deserialize(string topic, ReadOnlySpan<byte> data, bool isNull)
        {
            return BitConverter.ToInt32(data.ToArray(), 0);
        }

        public static byte[] Serialize(string topic, int val)
        {
            return BitConverter.GetBytes(val);
        }
    }
}
