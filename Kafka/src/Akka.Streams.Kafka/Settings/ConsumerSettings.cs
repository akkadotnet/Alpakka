using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Settings
{
    public sealed class ConsumerSettings<TKey, TValue>
    {
        public static ConsumerSettings<TKey, TValue> Create(ActorSystem system, Deserializer<TKey> keyDeserializer, Deserializer<TValue> valueDeserializer)
        {
            var config = system.Settings.Config.GetConfig("akka.kafka.consumer");
            return Create(config, keyDeserializer, valueDeserializer);
        }

        public static ConsumerSettings<TKey, TValue> Create(Config config, Deserializer<TKey> keyDeserializer, Deserializer<TValue> valueDeserializer)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config), "Kafka config for Akka.NET consumer was not provided");

            return new ConsumerSettings<TKey, TValue>(
                keyDeserializer: keyDeserializer,
                valueDeserializer: valueDeserializer,
                pollInterval: config.GetTimeSpan("poll-interval", TimeSpan.FromMilliseconds(50)),
                pollTimeout: config.GetTimeSpan("poll-timeout", TimeSpan.FromMilliseconds(50)),
                bufferSize: config.GetInt("buffer-size", 50),
                dispatcherId: config.GetString("use-dispatcher", "akka.kafka.default-dispatcher"),
                properties: ImmutableDictionary<string, string>.Empty,
                addEofMessage: config.GetBoolean("add-eof-message", false));
        }

        public object this[string propertyKey] => this.Properties.GetValueOrDefault(propertyKey);

        public Deserializer<TKey> KeyDeserializer { get; }
        public Deserializer<TValue> ValueDeserializer { get; }
        public TimeSpan PollInterval { get; }
        public TimeSpan PollTimeout { get; }
        public int BufferSize { get; }
        public string DispatcherId { get; }
        public IImmutableDictionary<string, string> Properties { get; }
        public bool AddEofMessage { get; }

        public ConsumerSettings(Deserializer<TKey> keyDeserializer, Deserializer<TValue> valueDeserializer, TimeSpan pollInterval, TimeSpan pollTimeout, int bufferSize, string dispatcherId, IImmutableDictionary<string, string> properties, bool addEofMessage)
        {
            KeyDeserializer = keyDeserializer;
            ValueDeserializer = valueDeserializer;
            PollInterval = pollInterval;
            PollTimeout = pollTimeout;
            BufferSize = bufferSize;
            DispatcherId = dispatcherId;
            Properties = properties;
            AddEofMessage = addEofMessage;
        }

        public ConsumerSettings<TKey, TValue> WithBootstrapServers(string bootstrapServers) =>
            Copy(properties: Properties.SetItem("bootstrap.servers", bootstrapServers));

        public ConsumerSettings<TKey, TValue> WithClientId(string clientId) =>
            Copy(properties: Properties.SetItem("client.id", clientId));

        public ConsumerSettings<TKey, TValue> WithGroupId(string groupId) =>
            Copy(properties: Properties.SetItem("group.id", groupId));

        public ConsumerSettings<TKey, TValue> WithProperty(string key, string value) =>
            Copy(properties: Properties.SetItem(key, value));

        public ConsumerSettings<TKey, TValue> WithPollInterval(TimeSpan pollInterval) => Copy(pollInterval: pollInterval);

        public ConsumerSettings<TKey, TValue> WithPollTimeout(TimeSpan pollTimeout) => Copy(pollTimeout: pollTimeout);

        public ConsumerSettings<TKey, TValue> WithDispatcher(string dispatcherId) => Copy(dispatcherId: dispatcherId);

        public ConsumerSettings<TKey, TValue> WithEofMessages(bool addEofMessage) => Copy(addEofMessage: addEofMessage);

        private ConsumerSettings<TKey, TValue> Copy(
            Deserializer<TKey> keyDeserializer = null,
            Deserializer<TValue> valueDeserializer = null,
            TimeSpan? pollInterval = null,
            TimeSpan? pollTimeout = null,
            int? bufferSize = null,
            string dispatcherId = null,
            IImmutableDictionary<string, string> properties = null,
            bool? addEofMessage = null) =>
            new ConsumerSettings<TKey, TValue>(
                keyDeserializer: keyDeserializer ?? KeyDeserializer,
                valueDeserializer: valueDeserializer ?? ValueDeserializer,
                pollInterval: pollInterval ?? PollInterval,
                pollTimeout: pollTimeout ?? PollTimeout,
                bufferSize: bufferSize ?? BufferSize,
                dispatcherId: dispatcherId ?? DispatcherId,
                properties: properties ?? Properties,
                addEofMessage: addEofMessage ?? AddEofMessage);

        public IConsumer<TKey, TValue> CreateKafkaConsumer() =>
            new Consumer<TKey, TValue>(Properties, KeyDeserializer, ValueDeserializer);
    }
}
