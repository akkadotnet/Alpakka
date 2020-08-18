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
            private readonly Outlet<T> _outlet;
            private readonly IAmqpSourceSettings<T> _amqpSourceSettings;
            private readonly ReceiverLink _receiver;
            private readonly Decider _decider;
            private readonly Queue<Message> _queue = new Queue<Message>();

            public AmqpSourceStageLogic(AmqpSourceStage<T> stage, Attributes attributes) : base(stage.Shape)
            {
                _outlet = stage.Out;
                _amqpSourceSettings = stage.AmqpSourceSettings;
                _receiver = stage.AmqpSourceSettings.GetReceiverLink();
                _decider = attributes.GetDeciderOrDefault();

                SetHandler(_outlet, () =>
                {
                    if (_queue.TryDequeue(out var msg))
                    {
                        PushMessage(msg);
                    }
                }, onDownstreamFinish: CompleteStage);
            }

            public override void PreStart()
            {
                base.PreStart();

                var consumerCallback = GetAsyncCallback<Message>(HandleDelivery);
                _receiver.Start(_amqpSourceSettings.Credit, (_, m) => consumerCallback.Invoke(m));
            }

            private void HandleDelivery(Message message)
            {
                _queue.Enqueue(message);
                //as callback could be called concurrently try to dequeue
                //a pull can be waiting for the message
                if (IsAvailable(_outlet) && _queue.TryDequeue(out var msg))
                {
                    PushMessage(msg);
                }
            }

            private void PushMessage(Message message)
            {
                T obj;
                try
                {
                    obj = _amqpSourceSettings.Convert(message);
                    _receiver.Accept(message);
                }
                catch (Exception e)
                {
                    if (_decider(e) == Directive.Stop)
                    {
                        _receiver.Reject(message, new Error(new Symbol(e.Message)));
                        FailStage(e);
                    }
                    return;
                }
                Push(_outlet, obj);
            }

            public override void PostStop()
            {
                _receiver.Close();
                base.PostStop();
            }
        }
    }
}
