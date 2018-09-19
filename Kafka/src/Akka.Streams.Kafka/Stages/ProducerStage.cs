using System;
using System.Threading.Tasks;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Stage;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Stages
{
    internal sealed class ProducerStage<K, V> : GraphStage<FlowShape<MessageAndMeta<K, V>, Task<DeliveryReport<K, V>>>>
    {
        public ProducerSettings<K, V> Settings { get; }
        public bool CloseProducerOnStop { get; }
        public Func<IProducer<K, V>> ProducerProvider { get; }
        public Inlet<MessageAndMeta<K, V>> In { get; } = new Inlet<MessageAndMeta<K, V>>("kafka.producer.in");
        public Outlet<Task<DeliveryReport<K, V>>> Out { get; } = new Outlet<Task<DeliveryReport<K, V>>>("kafka.producer.out");

        public ProducerStage(
            ProducerSettings<K, V> settings,
            bool closeProducerOnStop,
            Func<IProducer<K, V>> producerProvider)
        {
            Settings = settings;
            CloseProducerOnStop = closeProducerOnStop;
            ProducerProvider = producerProvider;
            Shape = new FlowShape<MessageAndMeta<K, V>, Task<DeliveryReport<K, V>>>(In, Out);
        }

        public override FlowShape<MessageAndMeta<K, V>, Task<DeliveryReport<K, V>>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ProducerStageLogic<K, V>(this, inheritedAttributes);
        }
    }
}
