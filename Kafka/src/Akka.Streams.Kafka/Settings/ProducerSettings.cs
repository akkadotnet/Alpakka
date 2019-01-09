using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Settings
{
    public sealed class ProducerSettings<TKey, TValue>
    {
        public ProducerSettings(Serializer<TKey> keySerializer, Serializer<TValue> valueSerializer, int parallelism, string dispatcherId, TimeSpan flushTimeout, IImmutableDictionary<string, string> properties)
        {
            KeySerializer = keySerializer;
            ValueSerializer = valueSerializer;
            Parallelism = parallelism;
            DispatcherId = dispatcherId;
            FlushTimeout = flushTimeout;
            Properties = properties;
        }

        public Serializer<TKey> KeySerializer { get; }
        public Serializer<TValue> ValueSerializer { get; }
        public int Parallelism { get; }
        public string DispatcherId { get; }
        public TimeSpan FlushTimeout { get; }
        public IImmutableDictionary<string, string> Properties { get; }

        public ProducerSettings<TKey, TValue> WithBootstrapServers(string bootstrapServers) =>
            WithProperty("bootstrap.servers", bootstrapServers);

        public ProducerSettings<TKey, TValue> WithProperty(string key, string value) =>
            Copy(properties: Properties.SetItem(key, value));

        public ProducerSettings<TKey, TValue> WithParallelism(int parallelism) =>
            Copy(parallelism: parallelism);

        public ProducerSettings<TKey, TValue> WithDispatcher(string dispatcherId) =>
            Copy(dispatcherId: dispatcherId);

        private ProducerSettings<TKey, TValue> Copy(
            Serializer<TKey> keySerializer = null,
            Serializer<TValue> valueSerializer = null,
            int? parallelism = null,
            string dispatcherId = null,
            TimeSpan? flushTimeout = null,
            IImmutableDictionary<string, string> properties = null) =>
            new ProducerSettings<TKey, TValue>(
                keySerializer: keySerializer ?? this.KeySerializer,
                valueSerializer: valueSerializer ?? this.ValueSerializer,
                parallelism: parallelism ?? this.Parallelism,
                dispatcherId: dispatcherId ?? this.DispatcherId,
                flushTimeout: flushTimeout ?? this.FlushTimeout,
                properties: properties ?? this.Properties);

        public static ProducerSettings<TKey, TValue> Create(ActorSystem system, Serializer<TKey> keySerializer, Serializer<TValue> valueSerializer)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));

            var config = system.Settings.Config.GetConfig("akka.kafka.producer");
            return Create(config, keySerializer, valueSerializer);
        }

        public static ProducerSettings<TKey, TValue> Create(Config config, Serializer<TKey> keySerializer, Serializer<TValue> valueSerializer)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "Kafka config for Akka.NET producer was not provided");
            
            return new ProducerSettings<TKey, TValue>(
                keySerializer: keySerializer,
                valueSerializer: valueSerializer,
                parallelism: config.GetInt("parallelism", 100),
                dispatcherId: config.GetString("use-dispatcher", "akka.kafka.default-dispatcher"),
                flushTimeout: config.GetTimeSpan("flush-timeout", TimeSpan.FromSeconds(2)),
                properties: ImmutableDictionary<string, string>.Empty);
        }

        public IProducer<TKey, TValue> CreateKafkaProducer() =>
            new Producer<TKey, TValue>(Properties, KeySerializer, ValueSerializer);
    }
}
