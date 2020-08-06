using Akka.Streams.Stage;
using Amqp;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task>
    {
        public Inlet<T> In { get; }
        public override SinkShape<T> Shape { get; }
        public IAmpqSinkSettings<T> AmpqSourceSettings { get; }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        public AmqpSinkStage(IAmpqSinkSettings<T> ampqSourceSettings)
        {
            In = new Inlet<T>("AmqpSink.in");
            Shape = new SinkShape<T>(In);
            AmpqSourceSettings = ampqSourceSettings;
        }

        private class AmqpSinkStageLogic : GraphStageLogic
        {
            private readonly AmqpSinkStage<T> stage;
            private readonly TaskCompletionSource<Done> promise;
            private readonly SenderLink sender;

            public AmqpSinkStageLogic(AmqpSinkStage<T> amqpSinkStage, TaskCompletionSource<Done> promise, SinkShape<T> shape) : base(shape)
            {
                stage = amqpSinkStage;
                this.promise = promise;
                sender = amqpSinkStage.AmpqSourceSettings.GetSenderLink();

                SetHandler(stage.In, () =>
                {
                    var elem = Grab(stage.In);
                    sender.Send(new Message(amqpSinkStage.AmpqSourceSettings.GetBytes(elem)));
                    Pull(stage.In);
                },
                    onUpstreamFinish: () => promise.SetResult(Done.Instance),
                    onUpstreamFailure: ex => promise.SetException(ex)
                );
            }

            public override void PreStart()
            {
                base.PreStart();
                Pull(stage.In);
            }

            public override void PostStop()
            {
                sender.Close();
                base.PostStop();
            }
        }
    }
}
