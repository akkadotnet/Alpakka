using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp.RabbitMq
{
    /// <summary>
    /// Connects to an AMQP server upon materialization and sends incoming messages to the server.
    /// Each materialized sink will create one connection to the broker.
    /// </summary>
    /// <inheritdoc />
    public sealed class AmqpSinkStage : GraphStageWithMaterializedValue<SinkShape<OutgoingMessage>, Task>
    {
        public AmqpSinkSettings Settings { get; }

        public static readonly Attributes DefaultAttributes =
            Attributes.CreateName("AmqpSink")
                .And(ActorAttributes.CreateDispatcher("akka.stream.default-blocking-io-dispatcher"));

        public AmqpSinkStage(AmqpSinkSettings settings)
        {
            Settings = settings;
        }

        public readonly Inlet<OutgoingMessage> In = new Inlet<OutgoingMessage>("AmqpSink.in");
        public override SinkShape<OutgoingMessage> Shape => new SinkShape<OutgoingMessage>(In);

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        protected override Attributes InitialAttributes => DefaultAttributes;

        public override string ToString() => "AmqpSink";

        private class AmqpSinkStageLogic : AmqpConnectorLogic
        {
            private readonly AmqpSinkStage _stage;
            private Action<ShutdownEventArgs> _shutdownCallback;
            private readonly TaskCompletionSource<Done> _promise;

            public AmqpSinkStageLogic(AmqpSinkStage stage, TaskCompletionSource<Done> promise, Shape shape) : base(shape)
            {
                _promise = promise;
                _stage = stage;
                SetHandler(_stage.In, () =>
                {
                    var elem = Grab(_stage.In);
                    Channel.BasicPublish(
                        Exchange,
                        elem.RoutingKey ?? RoutingKey,
                        elem.Mandatory,
                        elem.Properties,
                        elem.Bytes.ToArray());
                    
                    if(_stage.Settings.WaitForConfirms)
                    {
                        // https://www.rabbitmq.com/docs/confirms#publisher-confirms - waiting for confirms from broker
                        Debug.Assert(_stage.Settings.WaitForConfirmsTimeout != null, "_stage.Settings.WaitForConfirmsTimeout != null");
                        Channel.WaitForConfirmsOrDie(_stage.Settings.WaitForConfirmsTimeout.Value);
                    }

                    Pull(_stage.In);
                }, onUpstreamFinish: () => { _promise.SetResult(Done.Instance); }, onUpstreamFailure: ex => { _promise.SetException(ex); });
            }

            public override IAmqpConnectorSettings Settings => _stage.Settings;

            public string Exchange => _stage.Settings.Exchange ?? "";

            public string RoutingKey => _stage.Settings.RoutingKey ?? "";

            public override IConnectionFactory ConnectionFactoryFrom(IAmqpConnectionSettings settings)
            {
                return AmqpConnector.ConnectionFactoryFrom(settings);
            }

            public override IConnection NewConnection(IConnectionFactory factory, IAmqpConnectionSettings settings) =>
                AmqpConnector.NewConnection(factory, settings);

            public override void WhenConnected()
            {
                _shutdownCallback = GetAsyncCallback<ShutdownEventArgs>(args =>
                {
                    var exception = ShutdownSignalException.FromArgs(args);
                    _promise.SetException(exception);
                    FailStage(exception);
                });

                Channel.ModelShutdown += OnChannelShutdown;

                Pull(_stage.In);
            }

            public override void OnFailure(Exception ex)
            {
                _promise.TrySetException(ex);
            }

            private void OnChannelShutdown(object sender, ShutdownEventArgs shutdownEventArgs)
            {
                _shutdownCallback?.Invoke(shutdownEventArgs);
            }

            public override void PostStop()
            {
                _promise.TrySetException(new ApplicationException("stage stopped unexpectedly"));
                if (Channel != null)
                    Channel.ModelShutdown -= OnChannelShutdown;
                base.PostStop(); //don't forget to call the base.PostStop()
            }
        }
    }
}