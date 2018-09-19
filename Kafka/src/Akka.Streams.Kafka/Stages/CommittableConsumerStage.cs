using System.Threading.Tasks;
using Akka.Streams.Kafka.Messages;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Stage;

namespace Akka.Streams.Kafka.Stages
{
    internal class CommittableConsumerStage<K, V> : GraphStageWithMaterializedValue<SourceShape<CommittableMessage<K, V>>, Task>
	{
        public Outlet<CommittableMessage<K, V>> Out { get; } = new Outlet<CommittableMessage<K, V>>("kafka.commitable.consumer.out");
        public override SourceShape<CommittableMessage<K, V>> Shape { get; }
        public ConsumerSettings<K, V> Settings { get; }
        public ISubscription Subscription { get; }

        public CommittableConsumerStage(ConsumerSettings<K, V> settings, ISubscription subscription)
        {
            Settings = settings;
            Subscription = subscription;
            Shape = new SourceShape<CommittableMessage<K, V>>(Out);
        }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<NotUsed>();
            return new LogicAndMaterializedValue<Task>(new KafkaCommittableSourceStage<K, V>(this, inheritedAttributes, completion), completion.Task);
        }
    }
}
