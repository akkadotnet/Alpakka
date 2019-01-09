using System;
using System.Text;

namespace Akka.Streams.Kafka.Tests.Integration
{
    public static class StringSerializer
    {
        public static string Deserialize(string topic, ReadOnlySpan<byte> data, bool isNull)
        {
            return isNull ? null : Encoding.UTF8.GetString(data.ToArray());
        }

        public static byte[] Serialize(string topic, string val)
        {
            if (val == null)
                return null;

            return Encoding.UTF8.GetBytes(val);
        }
    }
}
