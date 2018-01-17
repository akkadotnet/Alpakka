using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Amqp.Dsl;
using Akka.Streams.Stage;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Connects to an AMQP server upon materialization and consumes messages from it emitting them
    /// into the stream. Each materialized source will create one connection to the broker.
    /// As soon as an <see cref="IncomingMessage"/> is sent downstream, an ack for it is sent to the broker.
    /// </summary>
    public sealed class AmqpSourceStage : GraphStage<SourceShape<CommittableIncomingMessage>>
    {
        public static readonly Attributes DefaultAttributes = Attributes.CreateName("AmqpSource");
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

        public override SourceShape<CommittableIncomingMessage> Shape => new SourceShape<CommittableIncomingMessage>(Out);

        public readonly Outlet<CommittableIncomingMessage> Out = new Outlet<CommittableIncomingMessage>("AmqpSource.out");

        protected override Attributes InitialAttributes => DefaultAttributes;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new AmqpSourceStageLogic(this);

        public override string ToString() => "AmqpSource";

        private class AmqpSourceStageLogic : AmqpConnectorLogic
        {
            private readonly AmqpSourceStage _stage;
            private readonly Queue<CommittableIncomingMessage> _queue = new Queue<CommittableIncomingMessage>();
            private IBasicConsumer _amqpSourceConsumer;
            private int _unackedMessages;

            public AmqpSourceStageLogic(AmqpSourceStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(_stage.Out, () =>
                    {
                        if (_queue.Count > 0)
                        {
                            PushMessage(_queue.Dequeue());
                        }
                    },
                    onDownstreamFinish: () =>
                    {
                        SetKeepGoing(true);
                        if (_unackedMessages == 0) 
                            CompleteStage(); //TODO: check if this is right?? JVM implementation: if (unackedMessages == 0) super.onDownstreamFinish()
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
                var consumerCallback = GetAsyncCallback<CommittableIncomingMessage>(HandleDelivery);

                var shutdownCallback = GetAsyncCallback<ShutdownEventArgs>(args =>
                {
                    if (args != null)
                    {
                        FailStage(ShutdownSignalException.FromArgs(args));
                    }
                    else if (_unackedMessages == 0)
                    {
                        CompleteStage();
                    }
                });

                var commitCallback = GetAsyncCallback<CommitCallback>(callback =>
                {
                    switch (callback)
                    {
                        case AckArguments args:
                            Channel.BasicAck(args.DeliveryTag, args.Multiple);
                            if (--_unackedMessages == 0 && IsClosed(_stage.Out)) CompleteStage();
                            args.Commit();
                            break;

                        case NackArguments args:
                            Channel.BasicNack(args.DeliveryTag, args.Multiple, args.Requeue);
                            if (--_unackedMessages == 0 && IsClosed(_stage.Out)) CompleteStage();
                            args.Commit();
                            break;
                    }
                });

                _amqpSourceConsumer = new DefaultConsumer(consumerCallback, commitCallback, shutdownCallback);

                switch (Settings)
                {
                    case NamedQueueSourceSettings _:
                        var namedSourceSettings = (NamedQueueSourceSettings) Settings;
                        SetupNamedQueue(namedSourceSettings);
                        break;
                    case TemporaryQueueSourceSettings _:
                        var tempSettings = (TemporaryQueueSourceSettings) Settings;
                        SetupTmeporaryQueue(tempSettings);
                        break;
                }
            }

            public override void OnFailure(Exception ex)
            {
            }

            private void SetupNamedQueue(NamedQueueSourceSettings settings)
            {
                Channel.BasicConsume(settings.Queue,
                    false, // never auto-ack
                    settings.ConsumerTag, // consumer tag
                    settings.NoLocal,
                    settings.Exclusive,
                    settings.Arguments.ToDictionary(k => k.Key, val => val.Value), _amqpSourceConsumer);
            }

            private void SetupTmeporaryQueue(TemporaryQueueSourceSettings settings)
            {
                // this is a weird case that required dynamic declaration, the queue name is not known
                // up front, it is only useful for sources, so that's why it's not placed in the AmqpConnectorLogic
                var queueName = Channel.QueueDeclare().QueueName;
                Channel.QueueBind(queueName, settings.Exchange, settings.RoutingKey ?? "");
                Channel.BasicConsume(queueName, false, _amqpSourceConsumer);
            }

            private void HandleDelivery(CommittableIncomingMessage message)
            {
                if (IsAvailable(_stage.Out))
                {
                    PushMessage(message);
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

            private void PushMessage(CommittableIncomingMessage message)
            {
                Push(_stage.Out, message);
                _unackedMessages++;
            }

            private class DefaultConsumer : DefaultBasicConsumer
            {
                private readonly Action<CommittableIncomingMessage> _consumerCallback;
                private readonly Action<CommitCallback> _commitCallback;
                private readonly Action<ShutdownEventArgs> _shutdownCallback;

                public DefaultConsumer(Action<CommittableIncomingMessage> consumerCallback, Action<CommitCallback> commitCallback,
                    Action<ShutdownEventArgs> shutdownCallback)
                {
                    _consumerCallback = consumerCallback;
                    _commitCallback = commitCallback;
                    _shutdownCallback = shutdownCallback;
                }

                public override void HandleBasicDeliver(string consumerTag, ulong deliveryTag, bool redelivered, string exchange, string routingKey,
                    IBasicProperties properties, byte[] body)
                {
                    var envelope = Envelope.Create(deliveryTag, redelivered, exchange, routingKey);
                    var message = new IncomingMessage(ByteString.CopyFrom(body), envelope, properties);

                    var committableMessage =
                        new CommittableIncomingMessage(
                            message,
                            ack: multiple =>
                            {
                                var promise = new TaskCompletionSource<Done>();
                                _commitCallback(new AckArguments(message.Envelope.DeliveryTag, multiple, promise));
                                return promise.Task;
                            },
                            nack: (multiple, requeue) =>
                            {
                                var promise = new TaskCompletionSource<Done>();
                                _commitCallback(new NackArguments(message.Envelope.DeliveryTag, multiple, requeue, promise));
                                return promise.Task;
                            }
                        );

                    _consumerCallback?.Invoke(committableMessage);
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