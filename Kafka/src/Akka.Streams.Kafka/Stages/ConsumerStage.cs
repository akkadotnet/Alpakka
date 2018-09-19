using System.Threading.Tasks;
using Akka.Streams.Kafka.Messages;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Stage;

namespace Akka.Streams.Kafka.Stages
{
    internal class KafkaSourceStage<K, V> : GraphStageWithMaterializedValue<SourceShape<ConsumerMessage<K, V>>, Task>
    {
        public Outlet<ConsumerMessage<K, V>> Out { get; } = new Outlet<ConsumerMessage<K, V>>("kafka.consumer.out");
        public override SourceShape<ConsumerMessage<K, V>> Shape { get; }
        public ConsumerSettings<K, V> Settings { get; }
        public ISubscription Subscription { get; }

        public KafkaSourceStage(ConsumerSettings<K, V> settings, ISubscription subscription)
        {
            Settings = settings;
            Subscription = subscription;
            Shape = new SourceShape<ConsumerMessage<K, V>>(Out);
            Settings = settings;
            Subscription = subscription;
        }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<NotUsed>();
            return new LogicAndMaterializedValue<Task>(new KafkaSourceStageLogic<K, V>(this, inheritedAttributes, completion), completion.Task);
        }
    }
}
