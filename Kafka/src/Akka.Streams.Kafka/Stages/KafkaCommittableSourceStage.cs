using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Kafka.Messages;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Stage;
using Confluent.Kafka;
using Akka.Streams.Supervision;

namespace Akka.Streams.Kafka.Stages
{
    internal class KafkaCommittableSourceStage<K, V> : TimerGraphStageLogic
    {
        private readonly ConsumerSettings<K, V> _settings;
        private readonly ISubscription _subscription;
        private readonly Outlet<CommittableMessage<K, V>> _out;
        private IConsumer<K, V> _consumer;

        private Action<IEnumerable<TopicPartition>> _partitionsAssigned;
        private Action<IEnumerable<TopicPartition>> _partitionsRevoked;
        private Action<TopicPartitionOffset> _partitionEof;

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

            _consumer.OnError += HandleOnError;
            _consumer.OnPartitionsAssigned += HandleOnPartitionsAssigned;
            _consumer.OnPartitionsRevoked += HandleOnPartitionsRevoked;
            if (_settings.AddEofMessage && _consumer is Consumer<K, V> consumer)
            {
                consumer.OnPartitionEOF += HandleOnPartitionEOF;
            }

            _subscription.AssignConsumer(_consumer);

            _partitionsAssigned = GetAsyncCallback<IEnumerable<TopicPartition>>(PartitionsAssigned);
            _partitionsRevoked = GetAsyncCallback<IEnumerable<TopicPartition>>(PartitionsRevoked);
            _partitionEof = GetAsyncCallback<TopicPartitionOffset>(PartitionEOF);
            ScheduleRepeatedly(TimerKey, _settings.PollInterval);
        }

        public override void PostStop()
        {
            _consumer.OnError -= HandleOnError;
            _consumer.OnPartitionsAssigned -= HandleOnPartitionsAssigned;
            _consumer.OnPartitionsRevoked -= HandleOnPartitionsRevoked;
            if (_settings.AddEofMessage && _consumer is Consumer<K, V> consumer)
            {
                consumer.OnPartitionEOF -= HandleOnPartitionEOF;
            }

            Log.Debug($"Consumer stopped: {_consumer.Name}");
            _consumer.Dispose();

            base.PostStop();
        }

        //
        // Consumer's events
        //

        private void HandleOnError(object sender, Error error)
        {
            Log.Error(error.Reason);
            //ANDSTE: I am in doubt; timeout exceptions are not reasons to fail the stage, as a closed connection
            //will stil work to produce messages on.
            //no need to fail stage
            return;

#pragma warning disable CS0162 // Unreachable code detected
            if (!KafkaExtensions.IsBrokerErrorRetriable(error) && !KafkaExtensions.IsLocalErrorRetriable(error))
            {
                var exception = new KafkaException(error);
                FailStage(exception);
            }
#pragma warning restore CS0162 // Unreachable code detected
        }

        private void HandleOnPartitionsAssigned(object sender, List<TopicPartition> list)
        {
            _partitionsAssigned(list);
        }

        private void HandleOnPartitionsRevoked(object sender, List<TopicPartition> list)
        {
            _partitionsRevoked(list);
        }

        private void HandleOnPartitionEOF(object sender, TopicPartitionOffset tpo)
        {
            _partitionEof(tpo);
        }

        //
        // Async callbacks
        //

        private void MessagesReceived(CommittableMessage<K, V> message)
        {
            _buffer.Enqueue(message);
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

        private void PartitionEOF(TopicPartitionOffset message)
        {
            Log.Debug($"Partition EOF triggered: {_consumer.Name}");
            if (_settings.AddEofMessage)
            {
                var consumer = _consumer;
                var commitableOffset = new CommitableOffset(
                    () => consumer.Commit(),
                    new PartitionOffset(_settings.Properties["group.id"], message.Topic, message.Partition, message.Offset));

                var msg = new ConsumeResult<K, V>
                {
                    Topic = message.Topic,
                    Partition = message.Partition,
                    Offset = message.Offset
                };
                MessagesReceived(new CommittableMessage<K, V>(msg, commitableOffset, MessageType.Eof));
            }
        }

        private void PullQueue()
        {
            var record = _consumer.Consume(_settings.PollTimeout);

            if (record != null)
            {
                var commitableOffset = new CommitableOffset(
                    () => _consumer.Commit(),
                    new PartitionOffset(_settings.Properties["group.id"], record.Topic, record.Partition, record.Offset));

                MessagesReceived(new CommittableMessage<K, V>(record, commitableOffset, MessageType.Eof));
            }

            if (!_isPaused && _buffer.Count > _settings.BufferSize)
            {
                Log.Debug("Polling paused, buffer is full");
                _consumer.Pause(_assignedPartitions);
                _isPaused = true;
            }
        }

        protected override void OnTimer(object timerKey) => PullQueue();
    }
}
