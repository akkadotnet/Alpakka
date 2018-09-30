using System;
using System.Threading.Tasks;
using Akka.Streams.Kafka.Settings;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Util.Internal;
using Confluent.Kafka;

namespace Akka.Streams.Kafka.Stages
{
    internal sealed class ProducerStageLogic<K, V> : GraphStageLogic
    {
        private readonly ProducerStage<K, V> _stage;
        private IProducer<K, V> _producer;

        private readonly TaskCompletionSource<NotUsed> _completionState;

        private volatile bool _inIsClosed;
        private readonly AtomicCounter _awaitingConfirmation = new AtomicCounter(0);

        private Action _checkCompleteCallback;
        private Action<Exception> _failStageCallback;

        private Exception _exceptionIfRaised;

        public ProducerStageLogic(ProducerStage<K, V> stage, Attributes attributes, TaskCompletionSource<NotUsed> completion) : base(stage.Shape)
        {
            _completionState = completion;
            _stage = stage;

            var supervisionStrategy = attributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
            var decider = supervisionStrategy != null ? supervisionStrategy.Decider : Deciders.ResumingDecider;

            SetHandler(_stage.In,
                onPush: () =>
                {
                    var msg = Grab(_stage.In);
                    var result = new TaskCompletionSource<DeliveryReport<K, V>>();

                    _producer.Produce(msg.TopicPartition, msg.Message, report =>
                    {
                        if (!report.Error.HasError)
                        {
                            result.SetResult(report);
                        }
                        else
                        {
                            var exception = new KafkaException(report.Error);
                            switch (decider(exception))
                            {
                                case Directive.Stop:
                                    _failStageCallback(exception);
                                    break;
                                default:
                                    result.SetException(exception);
                                    break;
                            }
                        }

                        if (_awaitingConfirmation.DecrementAndGet() == 0 && _inIsClosed)
                        {
                            _checkCompleteCallback();
                        }
                    });

                    _awaitingConfirmation.IncrementAndGet();
                    Push(_stage.Out, result.Task);
                },
                onUpstreamFinish: () =>
                {
                    _inIsClosed = true;
                    CheckForCompletion();
                },
                onUpstreamFailure: exception =>
                {
                    _inIsClosed = true;
                    _exceptionIfRaised = exception;
                    CheckForCompletion();
                });

            SetHandler(_stage.Out, onPull: () => TryPull(_stage.In));
        }

        public override void PreStart()
        {
            base.PreStart();

            _producer = _stage.ProducerProvider();

            _checkCompleteCallback = GetAsyncCallback(CheckForCompletion);
            _failStageCallback = GetAsyncCallback<Exception>(FailStage);

            Log.Debug($"Producer started: {_producer.Name}");
            _producer.OnError += OnProducerError;
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

            if (_exceptionIfRaised != null)
            {
                _completionState.SetException(_exceptionIfRaised);
            }
            else
            {
                _completionState.SetResult(NotUsed.Instance);
            }
            base.PostStop();
        }

        private void OnProducerError(object sender, Error error)
        {
            Log.Error($"{error.Code}: {error.Reason}");
            //ANDSTE: I am in doubt; timeout exceptions are not reasons to fail the stage, as a closed connection
            //will stil work to produce messages on.
            //no need to fail stage
            //return;

            if (!KafkaExtensions.IsBrokerErrorRetriable(error) && !KafkaExtensions.IsLocalErrorRetriable(error))
            {
                var exception = new KafkaException(error);
                FailStage(exception);
            }
        }

        public void CheckForCompletion()
        {
            if (IsClosed(_stage.In) && _awaitingConfirmation.Current == 0)
            {
                if (_exceptionIfRaised != null)
                {
                    FailStage(_exceptionIfRaised);
                }
                else
                {
                    CompleteStage();
                }
            }
        }
    }
}
