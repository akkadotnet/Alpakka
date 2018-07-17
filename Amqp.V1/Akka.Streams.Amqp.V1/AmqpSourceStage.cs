using Akka.Streams.Amqp.V1.Util;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using System;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSourceStage<T> : GraphStage<SourceShape<T>>
    {
        public AmqpSourceStage(IAmqpSourceSettings<T> amqpSourceSettings)
        {
            AmqpSourceSettings = amqpSourceSettings;
        }

        public override SourceShape<T> Shape => new SourceShape<T>(Out);

        public Outlet<T> Out { get; } = new Outlet<T>("AmqpSource.Out");
        public IAmqpSourceSettings<T> AmqpSourceSettings { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new AmqpSourceStageLogic(this, inheritedAttributes);

        private class AmqpSourceStageLogic : GraphStageLogic
        {
            private readonly Outlet<T> outlet;
            private readonly IAmqpSourceSettings<T> ampqSourceSettings;
            private readonly ReceiverLink receiver;
            private readonly Decider decider;

            public AmqpSourceStageLogic(AmqpSourceStage<T> stage, Attributes attributes) : base(stage.Shape)
            {
                outlet = stage.Out;
                ampqSourceSettings = stage.AmqpSourceSettings;
                receiver = stage.AmqpSourceSettings.GetReceiverLink();
                decider = attributes.GetDeciderOrDefault();

                SetHandler(outlet, () => {
                    var message = receiver.Receive();
                    if (message != null)
                    {
                        try
                        {
                            Push(outlet, ampqSourceSettings.Convert(message));
                            receiver.Accept(message);
                        }
                        catch (Exception e)
                        {
                            receiver.Reject(message, new Error(new Symbol(e.Message)));
                            if (decider(e) == Directive.Stop)
                            {
                                FailStage(e);
                            }
                        }

                    }
                }, onDownstreamFinish: CompleteStage);
            }

            public override void PostStop()
            {
                base.PostStop();
                receiver.Close();
            }
        }
    }
}
