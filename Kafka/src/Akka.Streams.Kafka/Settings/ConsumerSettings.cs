using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Confluent.Kafka.Serialization;

namespace Akka.Streams.Kafka.Settings
{
    public sealed class ConsumerSettings<TKey, TValue>
    {
        public static ConsumerSettings<TKey, TValue> Create(ActorSystem system, IDeserializer<TKey> keyDeserializer, IDeserializer<TValue> valueDeserializer)
        {
            var config = system.Settings.Config.GetConfig("akka.kafka.consumer");
            return Create(config, keyDeserializer, valueDeserializer);
        }

        public static ConsumerSettings<TKey, TValue> Create(Config config, IDeserializer<TKey> keyDeserializer, IDeserializer<TValue> valueDeserializer)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "Kafka config for Akka.NET consumer was not provided");

            return new ConsumerSettings<TKey, TValue>(
                keyDeserializer: keyDeserializer,
                valueDeserializer: valueDeserializer,
                pollInterval: config.GetTimeSpan("poll-interval", TimeSpan.FromMilliseconds(50)),
                pollTimeout: config.GetTimeSpan("poll-timeout", TimeSpan.FromMilliseconds(50)),
                bufferSize: config.GetInt("buffer-size", 50),
                dispatcherId: config.GetString("use-dispatcher", "akka.kafka.default-dispatcher"),
                properties: ImmutableDictionary<string, object>.Empty,
                addEofMessage: config.GetBoolean("add-eof-message", false));
        }

        public object this[string propertyKey] => this.Properties.GetValueOrDefault(propertyKey);

        public IDeserializer<TKey> KeyDeserializer { get; }
        public IDeserializer<TValue> ValueDeserializer { get; }
        public TimeSpan PollInterval { get; }
        public TimeSpan PollTimeout { get; }
        public int BufferSize { get; }
        public string DispatcherId { get; }
        public IImmutableDictionary<string, object> Properties { get; }
        public bool AddEofMessage { get; }

        public ConsumerSettings(IDeserializer<TKey> keyDeserializer, IDeserializer<TValue> valueDeserializer, TimeSpan pollInterval, TimeSpan pollTimeout, int bufferSize, string dispatcherId, IImmutableDictionary<string, object> properties, bool addEofMessage)
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

        public ConsumerSettings<TKey, TValue> WithProperty(string key, object value) =>
            Copy(properties: Properties.SetItem(key, value));

        public ConsumerSettings<TKey, TValue> WithPollInterval(TimeSpan pollInterval) => Copy(pollInterval: pollInterval);

        public ConsumerSettings<TKey, TValue> WithPollTimeout(TimeSpan pollTimeout) => Copy(pollTimeout: pollTimeout);

        public ConsumerSettings<TKey, TValue> WithDispatcher(string dispatcherId) => Copy(dispatcherId: dispatcherId);

        public ConsumerSettings<TKey, TValue> WithEofMessages(bool addEofMessage) => Copy(addEofMessage: addEofMessage);

        private ConsumerSettings<TKey, TValue> Copy(
            IDeserializer<TKey> keyDeserializer = null,
            IDeserializer<TValue> valueDeserializer = null,
            TimeSpan? pollInterval = null,
            TimeSpan? pollTimeout = null,
            int? bufferSize = null,
            string dispatcherId = null,
            IImmutableDictionary<string, object> properties = null,
            bool? addEofMessage = null) =>
            new ConsumerSettings<TKey, TValue>(
                keyDeserializer: keyDeserializer ?? this.KeyDeserializer,
                valueDeserializer: valueDeserializer ?? this.ValueDeserializer,
                pollInterval: pollInterval ?? this.PollInterval,
                pollTimeout: pollTimeout ?? this.PollTimeout,
                bufferSize: bufferSize ?? this.BufferSize,
                dispatcherId: dispatcherId ?? this.DispatcherId,
                properties: properties ?? this.Properties,
                addEofMessage: addEofMessage ?? this.AddEofMessage);

        public Confluent.Kafka.IConsumer<TKey, TValue> CreateKafkaConsumer() =>
            new Confluent.Kafka.Consumer<TKey, TValue>(this.Properties, this.KeyDeserializer, this.ValueDeserializer);
    }
}
