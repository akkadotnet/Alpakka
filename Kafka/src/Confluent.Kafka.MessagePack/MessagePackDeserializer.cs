using System.Collections.Generic;
using Confluent.Kafka.Serialization;
using MessagePack.Resolvers;

namespace Confluent.Kafka.MessagePack
{
    public sealed class MsgPackDeserializer<T> : IDeserializer<T>
    {
        public T Deserialize(string topic, byte[] data)
        {
            return global::MessagePack.MessagePackSerializer.Deserialize<T>(data, ContractlessStandardResolver.Instance);
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
