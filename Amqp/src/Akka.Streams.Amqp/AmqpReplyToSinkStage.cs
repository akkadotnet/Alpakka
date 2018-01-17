using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Connects to an AMQP server upon materialization and sends incoming messages to the server.
    /// Each materialized sink will create one connection to the broker. This stage sends messages to
    /// the queue named in the replyTo options of the message instead of from settings declared at construction.
    /// </summary>
    /// <inheritdoc />
    public sealed class AmqpReplyToSinkStage : GraphStageWithMaterializedValue<SinkShape<OutgoingMessage>, Task>
    {
        public AmqpReplyToSinkSettings Settings { get; }

        public static readonly Attributes DefaultAttributes =
            Attributes.CreateName("AmqpReplyToSink")
                .And(ActorAttributes.CreateDispatcher("akka.stream.default-blocking-io-dispatcher"));

        public AmqpReplyToSinkStage(AmqpReplyToSinkSettings settings)
        {
            Settings = settings;
        }

        public readonly Inlet<OutgoingMessage> In = new Inlet<OutgoingMessage>("AmqpReplyToSink.in");
        public override SinkShape<OutgoingMessage> Shape => new SinkShape<OutgoingMessage>(In);

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        protected override Attributes InitialAttributes => DefaultAttributes;

        public override string ToString() => "AmqpReplyToSink";

        private class AmqpSinkStageLogic : AmqpConnectorLogic
        {
            private readonly AmqpReplyToSinkStage _stage;
            private Action<ShutdownEventArgs> _shutdownCallback;
            private readonly TaskCompletionSource<Done> _promise;

            public AmqpSinkStageLogic(AmqpReplyToSinkStage stage, TaskCompletionSource<Done> promise, Shape shape) : base(shape)
            {
                _promise = promise;
                _stage = stage;
                SetHandler(_stage.In,
                    onPush: () =>
                    {
                        var elem = Grab(_stage.In);
                        if (!string.IsNullOrWhiteSpace(elem.Properties?.ReplyTo))
                        {
                            Channel.BasicPublish(
                                elem.RoutingKey ?? "",
                                elem.Properties?.ReplyTo,
                                elem.Mandatory,
                                elem.Properties,
                                elem.Bytes.ToArray());
                        }
                        else if (_stage.Settings.FailIfReplyToMissing)
                        {
                            var ex = new Exception("Reply-to header was not set");
                            _promise.TrySetException(ex);
                            FailStage(ex);
                        }

                        Pull(_stage.In);
                    },
                    onUpstreamFinish: () => { _promise.TrySetResult(Done.Instance); },
                    onUpstreamFailure: ex => { _promise.TrySetException(ex); });
            }

            public override IAmqpConnectorSettings Settings => _stage.Settings;

            public override IConnectionFactory ConnectionFactoryFrom(IAmqpConnectionSettings settings) =>
                AmqpConnector.ConnectionFactoryFrom(settings);

            public override IConnection NewConnection(IConnectionFactory factory, IAmqpConnectionSettings settings) =>
                AmqpConnector.NewConnection(factory, settings);

            public override void WhenConnected()
            {
                _shutdownCallback = GetAsyncCallback<ShutdownEventArgs>(args =>
                {
                    var exception = ShutdownSignalException.FromArgs(args);
                    _promise.TrySetException(exception);
                    FailStage(exception);
                });

                Channel.ModelShutdown += OnChannelShutdown;

                Pull(_stage.In);
            }

            public override void OnFailure(Exception ex) => _promise.TrySetException(ex);

            private void OnChannelShutdown(object sender, ShutdownEventArgs shutdownEventArgs)
            {
                _shutdownCallback?.Invoke(shutdownEventArgs);
            }

            public override void PostStop()
            {
                _promise.TrySetException(new ApplicationException("stage stopped unexpectedly"));
                Channel.ModelShutdown -= OnChannelShutdown;
                base.PostStop(); //don't forget to call the base.PostStop()
            }
        }
    }
}