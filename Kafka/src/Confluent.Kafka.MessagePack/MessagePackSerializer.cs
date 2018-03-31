using System.Collections.Generic;
using Confluent.Kafka.Serialization;
using MessagePack.Resolvers;

namespace Confluent.Kafka.MessagePack
{
    public sealed class MessagePackSerializer<T> : ISerializer<T>
    {
        public byte[] Serialize(string topic, T data)
        {
            return global::MessagePack.MessagePackSerializer.Serialize<T>(data, ContractlessStandardResolver.Instance);
        }

        public IEnumerable<KeyValuePair<string, object>> Configure(IEnumerable<KeyValuePair<string, object>> config, bool isKey)
        {
            return config;
        }

        public void Dispose()
        {
        }
    }
}
