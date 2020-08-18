using Akka.Streams.Stage;
using Amqp;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task>
    {
        public Inlet<T> In { get; }
        public override SinkShape<T> Shape { get; }
        public IAmqpSinkSettings<T> AmqpSourceSettings { get; }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        public AmqpSinkStage(IAmqpSinkSettings<T> amqpSourceSettings)
        {
            In = new Inlet<T>("AmqpSink.in");
            Shape = new SinkShape<T>(In);
            AmqpSourceSettings = amqpSourceSettings;
        }

        private class AmqpSinkStageLogic : GraphStageLogic
        {
            private readonly AmqpSinkStage<T> _stage;
            private readonly TaskCompletionSource<Done> _promise;
            private readonly SenderLink _sender;

            public AmqpSinkStageLogic(AmqpSinkStage<T> amqpSinkStage, TaskCompletionSource<Done> promise, SinkShape<T> shape) : base(shape)
            {
                _stage = amqpSinkStage;
                _promise = promise;
                _sender = amqpSinkStage.AmqpSourceSettings.GetSenderLink();

                SetHandler(
                    inlet: _stage.In, 
                    onPush: () =>
                    {
                        var elem = Grab(_stage.In);
                        _sender.Send(new Message(amqpSinkStage.AmqpSourceSettings.GetBytes(elem)));
                        Pull(_stage.In);
                    },
                    onUpstreamFinish: () => _promise.SetResult(Done.Instance),
                    onUpstreamFailure: _promise.SetException
                );
            }

            public override void PreStart()
            {
                base.PreStart();
                Pull(_stage.In);
            }

            public override void PostStop()
            {
                _sender.Close();
                base.PostStop();
            }
        }
    }
}
