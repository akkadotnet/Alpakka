using Akka.Streams.Stage;
using Amqp;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task>
    {
        public readonly Inlet<T> In = new Inlet<T>("AmqpSink.in");
        public override SinkShape<T> Shape => new SinkShape<T>(In);

        public IAmpqSinkSettings<T> AmpqSourceSettings { get; }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        public AmqpSinkStage(IAmpqSinkSettings<T> ampqSourceSettings)
        {
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
                    try
                    {
                        var elem = Grab(stage.In);
                        sender.Send(new Message(amqpSinkStage.AmpqSourceSettings.GetBytes(elem)));
                        Pull(stage.In);
                    } catch (Exception e)
                    {
                        Debugger.Break();
                    }
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
                promise.TrySetException(new ApplicationException("stage stopped unexpectedly"));
                sender.Close();
                base.PostStop();
            }
        }
    }
}
