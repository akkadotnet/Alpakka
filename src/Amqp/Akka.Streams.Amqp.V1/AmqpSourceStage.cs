using Akka.Streams.Amqp.V1.Util;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using System;
using System.Collections.Generic;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSourceStage<T> : GraphStage<SourceShape<T>>
    {
        public override SourceShape<T> Shape { get; }
        public Outlet<T> Out { get; }
        public IAmqpSourceSettings<T> AmqpSourceSettings { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new AmqpSourceStageLogic(this, inheritedAttributes);

        public AmqpSourceStage(IAmqpSourceSettings<T> amqpSourceSettings)
        {
            Out = new Outlet<T>("AmqpSource.Out");
            Shape = new SourceShape<T>(Out);
            AmqpSourceSettings = amqpSourceSettings;
        }

        private class AmqpSourceStageLogic : GraphStageLogic
        {
            private readonly Outlet<T> outlet;
            private readonly IAmqpSourceSettings<T> ampqSourceSettings;
            private readonly ReceiverLink receiver;
            private readonly Decider decider;
            private readonly Queue<Message> queue = new Queue<Message>();

            public AmqpSourceStageLogic(AmqpSourceStage<T> stage, Attributes attributes) : base(stage.Shape)
            {
                outlet = stage.Out;
                ampqSourceSettings = stage.AmqpSourceSettings;
                receiver = stage.AmqpSourceSettings.GetReceiverLink();
                decider = attributes.GetDeciderOrDefault();

                SetHandler(outlet, () =>
                {
                    if (queue.TryDequeue(out Message msg))
                    {
                        PushMessage(msg);
                    }
                }, onDownstreamFinish: CompleteStage);
            }

            public override void PreStart()
            {
                base.PreStart();

                var consumerCallback = GetAsyncCallback<Message>(HandleDelivery);
                receiver.Start(ampqSourceSettings.Credit, (_, m) => consumerCallback.Invoke(m));
            }

            private void HandleDelivery(Message message)
            {
                queue.Enqueue(message);
                //as callback could be called concurrently try to dequeue
                //a pull can be waiting for the message
                if (IsAvailable(outlet) && queue.TryDequeue(out Message msg))
                {
                    PushMessage(msg);
                }
            }

            private void PushMessage(Message message)
            {
                T obj = default(T);
                try
                {
                    obj = ampqSourceSettings.Convert(message);
                    receiver.Accept(message);
                }
                catch (Exception e)
                {
                    if (decider(e) == Directive.Stop)
                    {
                        receiver.Reject(message, new Error(new Symbol(e.Message)));
                        FailStage(e);
                    }
                    return;
                }
                Push(outlet, obj);
            }

            public override void PostStop()
            {
                receiver.Close();
                base.PostStop();
            }
        }
    }
}
