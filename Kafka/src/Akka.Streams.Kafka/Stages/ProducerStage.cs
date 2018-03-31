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

    internal sealed class ProducerStageLogic<K, V> : GraphStageLogic
    {
        private readonly ProducerStage<K, V> _stage;
        private IProducer<K, V> _producer;
        private readonly TaskCompletionSource<NotUsed> _completionState = new TaskCompletionSource<NotUsed>();
        private Action<MessageAndMeta<K, V>> _sendToProducer;

        public ProducerStageLogic(ProducerStage<K, V> stage, Attributes attributes) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(_stage.In, 
                onPush: () =>
                {
                    var msg = Grab(_stage.In);
                    _sendToProducer(msg);
                },
                onUpstreamFinish: () =>
                {
                    _completionState.SetResult(NotUsed.Instance);
                    _producer.Flush(_stage.Settings.FlushTimeout);
                    CheckForCompletion();
                },
                onUpstreamFailure: exception =>
                {
                    _completionState.SetException(exception);
                    CheckForCompletion();
                });

            SetHandler(_stage.Out, onPull: () =>
            {
                TryPull(_stage.In);
            });
        }

        public override void PreStart()
        {
            base.PreStart();

            _producer = _stage.ProducerProvider();
            Log.Debug($"Producer started: {_producer.Name}");

            _producer.OnError += OnProducerError;

            _sendToProducer = msg =>
            {
                var task = _producer.ProduceAsync(msg.TopicPartition, msg.Message);
                Push(_stage.Out, task);
            };
        }

        public override void PostStop()
        {
            Log.Debug("Stage completed");
            _producer.OnError -= OnProducerError;

            if (_stage.CloseProducerOnStop)
            {
                _producer.Flush(_stage.Settings.FlushTimeout);
                _producer.Dispose();
                Log.Debug($"Producer closed: {_producer.Name}");
            }

            base.PostStop();
        }

        private void OnProducerError(object sender, Error error)
        {
            Log.Error(error.Reason);

            if (!KafkaExtensions.IsBrokerErrorRetriable(error) && !KafkaExtensions.IsLocalErrorRetriable(error))
            {
                var exception = new KafkaException(error);
                FailStage(exception);
            }
        }

        public void CheckForCompletion()
        {
            if (IsClosed(_stage.In))
            {
                var completionTask = _completionState.Task;

                if (completionTask.IsFaulted || completionTask.IsCanceled)
                {
                    FailStage(completionTask.Exception);
                }
                else if (completionTask.IsCompleted)
                {
                    CompleteStage();
                }
            }
        }
    }
}
