using System;
using System.Collections.Generic;
using System.Linq;
using Akka.IO;
using Akka.Streams.Stage;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Connects to an AMQP server upon materialization and consumes messages from it emitting them
    /// into the stream. Each materialized source will create one connection to the broker.
    /// As soon as an <see cref="IncomingMessage"/> is sent downstream, an ack for it is sent to the broker.
    /// </summary>
    public sealed class AmqpSourceStage : GraphStage<SourceShape<IncomingMessage>>
    {
        public static Attributes DefaultAttributes = Attributes.CreateName("AmqpSource");
        public IAmqpSourceSettings Settings { get; }
        public int BufferSize { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="settings">The source settings</param>
        /// <param name="bufferSize">The max number of elements to prefetch and buffer at any given time.</param>
        public AmqpSourceStage(IAmqpSourceSettings settings, int bufferSize)
        {
            Settings = settings;
            BufferSize = bufferSize;
        }
        public override SourceShape<IncomingMessage> Shape => new SourceShape<IncomingMessage>(Out);
        public readonly Outlet<IncomingMessage> Out = new Outlet<IncomingMessage>("AmqpSource.out");
        protected override Attributes InitialAttributes => DefaultAttributes;
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new AmqpSourceStageLogic(this);
        }
        public override string ToString()
        {
            return "AmqpSource";
        }
        private class AmqpSourceStageLogic : AmqpConnectorLogic
        {
            private readonly AmqpSourceStage _stage;
            private readonly Queue<IncomingMessage> _queue = new Queue<IncomingMessage>();
            private IBasicConsumer _amqpSourceConsumer;
            
            public AmqpSourceStageLogic(AmqpSourceStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(_stage.Out, () =>
                {
                    if (_queue.Count > 0)
                    {
                        PushAndAckMessage(_queue.Dequeue());
                    }
                });
            }
            public override IAmqpConnectorSettings Settings => _stage.Settings;
            public override IConnectionFactory ConnectionFactoryFrom(IAmqpConnectionSettings settings) =>
                AmqpConnector.ConnectionFactoryFrom(settings);
            public override IConnection NewConnection(IConnectionFactory factory, IAmqpConnectionSettings settings) =>
                AmqpConnector.NewConnection(factory, settings);
            public override void WhenConnected()
            {
                // we have only one consumer per connection so global is ok
                Channel.BasicQos(0, (ushort) _stage.BufferSize, false);
                var consumerCallback = GetAsyncCallback<IncomingMessage>(HandleDelivery);
                var shutdownCallback = GetAsyncCallback<ShutdownEventArgs>(args =>
                {
                    if (args != null)
                    {
                        FailStage(ShutdownSignalException.FromArgs(args));
                    }
                    else
                    {
                        CompleteStage();
                    }
                });

                _amqpSourceConsumer = new DefaultConsumer(consumerCallback,shutdownCallback);

                if (Settings is NamedQueueSourceSettings)
                {
                    var namedSourceSettings = (NamedQueueSourceSettings) Settings;
                    SetupNamedQueue(namedSourceSettings);
                }
                else if (Settings is TemporaryQueueSourceSettings)
                {
                    var tempSettings = (TemporaryQueueSourceSettings) Settings;
                    SetupTmeporaryQueue(tempSettings);
                }
            }
            public override void OnFailure(Exception ex)
            {
            }
            private void SetupNamedQueue(NamedQueueSourceSettings settings)
            {
                Channel.BasicConsume(settings.Queue,
                    false,// never auto-ack
                    settings.ConsumerTag,// consumer tag
                    settings.NoLocal,
                    settings.Exclusive,
                    settings.Arguments.ToDictionary(k=> k.Key, val=> val.Value), _amqpSourceConsumer);
            }
            private void SetupTmeporaryQueue(TemporaryQueueSourceSettings settings)
            {
                // this is a weird case that required dynamic declaration, the queue name is not known
                // up front, it is only useful for sources, so that's why it's not placed in the AmqpConnectorLogic
                var queueName = Channel.QueueDeclare().QueueName;
                Channel.QueueBind(queueName, settings.Exchange, settings.RoutingKey ?? "");
                Channel.BasicConsume(queueName, false, _amqpSourceConsumer);
            }
            private void HandleDelivery(IncomingMessage message)
            {
                if (IsAvailable(_stage.Out))
                {
                    PushAndAckMessage(message);
                }
                else
                {
                    if (_queue.Count + 1 > _stage.BufferSize)
                    {
                        FailStage(new ApplicationException($"Reached maximum buffer size {_stage.BufferSize}"));
                    }
                    else
                    {
                        _queue.Enqueue(message);
                    }
                }
            }
            private void PushAndAckMessage(IncomingMessage message)
            {
                Push(_stage.Out, message);
                // ack it as soon as we have passed it downstream
                // TODO ack less often and do batch acks with multiple = true would probably be more performant
                Channel.BasicAck(message.Envelope.DeliveryTag,
                    false);// just this single message

            }
            private class DefaultConsumer : DefaultBasicConsumer
            {
                private readonly Action<IncomingMessage> _consumerCallback;
                private readonly Action<ShutdownEventArgs> _shutdownCallback;
                public DefaultConsumer(Action<IncomingMessage> consumerCallback, Action<ShutdownEventArgs> shutdownCallback)
                {
                    _consumerCallback = consumerCallback;
                    _shutdownCallback = shutdownCallback;
                }
                public override void HandleBasicDeliver(string consumerTag, ulong deliveryTag, bool redelivered, string exchange, string routingKey,
                    IBasicProperties properties, byte[] body)
                {
                    var envelope = Envelope.Create(deliveryTag, redelivered, exchange, routingKey);
                    var incomingMessage = IncomingMessage.Create(ByteString.CopyFrom(body), envelope, properties);
                    _consumerCallback?.Invoke(incomingMessage);
                }
                public override void HandleBasicCancel(string consumerTag)
                {
                    // non consumer initiated cancel, for example happens when the queue has been deleted.
                    _shutdownCallback?.Invoke(null);
                }
                public override void HandleModelShutdown(object model, ShutdownEventArgs reason)
                {
                    _shutdownCallback?.Invoke(reason);
                }
            }

        }
        
    }
}
