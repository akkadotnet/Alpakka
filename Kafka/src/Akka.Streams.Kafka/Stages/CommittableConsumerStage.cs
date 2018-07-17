using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Kafka.Messages;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Stage;
using Confluent.Kafka;
using Akka.Streams.Supervision;
using System.Runtime.Serialization;

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

    internal class KafkaCommittableSourceStage<K, V> : TimerGraphStageLogic
    {
        private readonly ConsumerSettings<K, V> _settings;
        private readonly ISubscription _subscription;
        private readonly Outlet<CommittableMessage<K, V>> _out;
        private IConsumer<K, V> _consumer;

        private Action<ConsumerRecord<K, V>> _messagesReceived;
        private Action<IEnumerable<TopicPartition>> _partitionsAssigned;
        private Action<IEnumerable<TopicPartition>> _partitionsRevoked;
        private readonly Decider _decider;

        private const string TimerKey = "PollTimer";

        private readonly Queue<CommittableMessage<K, V>> _buffer;
        private IEnumerable<TopicPartition> _assignedPartitions;
        private volatile bool _isPaused;
        private readonly TaskCompletionSource<NotUsed> _completion;

        public KafkaCommittableSourceStage(CommittableConsumerStage<K, V> stage, Attributes attributes, TaskCompletionSource<NotUsed> completion) : base(stage.Shape)
        {
            _settings = stage.Settings;
            _subscription = stage.Subscription;
            _out = stage.Out;
            _completion = completion;
            _buffer = new Queue<CommittableMessage<K, V>>(stage.Settings.BufferSize);

            var supervisionStrategy = attributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
            _decider = supervisionStrategy != null ? supervisionStrategy.Decider : Deciders.ResumingDecider;

            SetHandler(_out, onPull: () =>
            {
                if (_buffer.Count > 0)
                {
                    Push(_out, _buffer.Dequeue());
                }
                else
                {
                    if (_isPaused)
                    {
                        _consumer.Resume(_assignedPartitions);
                        _isPaused = false;
                        Log.Debug("Polling resumed, buffer is empty");
                    }
                    PullQueue();
                }
            });
        }

        public override void PreStart()
        {
            base.PreStart();

            _consumer = _settings.CreateKafkaConsumer();
            Log.Debug($"Consumer started: {_consumer.Name}");

            _consumer.OnRecord += HandleOnMessage;
            _consumer.OnConsumeError += HandleConsumeError;
            _consumer.OnError += HandleOnError;
            _consumer.OnPartitionsAssigned += HandleOnPartitionsAssigned;
            _consumer.OnPartitionsRevoked += HandleOnPartitionsRevoked;

            switch (_subscription)
            {
                case TopicSubscription ts:
                    _consumer.Subscribe(ts.Topics);
                    break;
                case Assignment a:
                    _consumer.Assign(a.TopicPartitions);
                    break;
                case AssignmentWithOffset awo:
                    _consumer.Assign(awo.TopicPartitions);
                    break;
            }

            _messagesReceived = GetAsyncCallback<ConsumerRecord<K, V>>(MessagesReceived);
            _partitionsAssigned = GetAsyncCallback<IEnumerable<TopicPartition>>(PartitionsAssigned);
            _partitionsRevoked = GetAsyncCallback<IEnumerable<TopicPartition>>(PartitionsRevoked);
            ScheduleRepeatedly(TimerKey, _settings.PollInterval);
        }

        public override void PostStop()
        {
            _consumer.OnRecord -= HandleOnMessage;
            _consumer.OnConsumeError -= HandleConsumeError;
            _consumer.OnError -= HandleOnError;
            _consumer.OnPartitionsAssigned -= HandleOnPartitionsAssigned;
            _consumer.OnPartitionsRevoked -= HandleOnPartitionsRevoked;

            Log.Debug($"Consumer stopped: {_consumer.Name}");
            _consumer.Dispose();

            base.PostStop();
        }

        //
        // Consumer's events
        //

        private void HandleOnMessage(object sender, ConsumerRecord<K, V> message) => _messagesReceived(message);

        private void HandleConsumeError(object sender, ConsumerRecord message)
        {
            Log.Error(message.Error.Reason);
            var exception = new SerializationException(message.Error.Reason);
            switch (_decider(exception))
            {
                case Directive.Stop:
                    // Throw
                    _completion.TrySetException(exception);
                    FailStage(exception);
                    break;
                case Directive.Resume:
                    // keep going
                    break;
                case Directive.Restart:
                    // keep going
                    break;
            }
        }

        private void HandleOnError(object sender, Error error)
        {
            Log.Error(error.Reason);

            if (!KafkaExtensions.IsBrokerErrorRetriable(error) && !KafkaExtensions.IsLocalErrorRetriable(error))
            {
                var exception = new KafkaException(error);
                FailStage(exception);
            }
        }

        private void HandleOnPartitionsAssigned(object sender, List<TopicPartition> list)
        {
            _partitionsAssigned(list);
        }

        private void HandleOnPartitionsRevoked(object sender, List<TopicPartition> list)
        {
            _partitionsRevoked(list);
        }

        //
        // Async callbacks
        //

        private void MessagesReceived(ConsumerRecord<K, V> message)
        {
            var consumer = _consumer;
            var commitableOffset = new CommitableOffset(
                () => consumer.Commit(),
                new PartitionOffset("groupId", message.Topic, message.Partition, message.Offset));

            _buffer.Enqueue(new CommittableMessage<K, V>(message, commitableOffset));
            if (IsAvailable(_out))
            {
                Push(_out, _buffer.Dequeue());
            }
        }

        private void PartitionsAssigned(IEnumerable<TopicPartition> partitions)
        {
            Log.Debug($"Partitions were assigned: {_consumer.Name}");
            _consumer.Assign(partitions);
            _assignedPartitions = partitions;
        }

        private void PartitionsRevoked(IEnumerable<TopicPartition> partitions)
        {
            Log.Debug($"Partitions were revoked: {_consumer.Name}");
            _consumer.Unassign();
            _assignedPartitions = null;
        }

        private void PullQueue()
        {
            _consumer.Poll(_settings.PollTimeout);

            if (!_isPaused && _buffer.Count > _settings.BufferSize)
            {
                Log.Debug($"Polling paused, buffer is full");
                _consumer.Pause(_assignedPartitions);
                _isPaused = true;
            }
        }

        protected override void OnTimer(object timerKey) => PullQueue();
    }
}
