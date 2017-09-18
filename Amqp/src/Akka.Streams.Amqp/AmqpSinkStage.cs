using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// Connects to an AMQP server upon materialization and sends incoming messages to the server.
    /// Each materialized sink will create one connection to the broker.
    /// </summary>
    public sealed class AmqpSinkStage : GraphStageWithMaterializedValue<SinkShape<OutgoingMessage>,Task<Done>>
    {
        public AmqpSinkSettings Settings { get;}

        public static Attributes DefaultAttributes =
            Attributes.CreateName("AmsqpSink")
                .And(ActorAttributes.CreateDispatcher("akka.stream.default-blocking-io-dispatcher"));

        public AmqpSinkStage(AmqpSinkSettings settings)
        {
            Settings = settings;
        }

        public Inlet<OutgoingMessage> In = new Inlet<OutgoingMessage>("AmqpSink.in"); 
        public override SinkShape<OutgoingMessage> Shape => new SinkShape<OutgoingMessage>(In);

        public override ILogicAndMaterializedValue<Task<Done>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task<Done>>(logic, promise.Task);
        }

        protected override Attributes InitialAttributes => DefaultAttributes;

        public override string ToString()
        {
            return "AmqpSink";
        }


        private class AmqpSinkStageLogic : AmqpConnectorLogic
        {
            private readonly AmqpSinkStage _stage;
            private Action<ShutdownEventArgs> _shutdownCallback;
            private readonly TaskCompletionSource<Done> _promise;
          
            public AmqpSinkStageLogic(AmqpSinkStage stage, TaskCompletionSource<Done> promise,Shape shape) : base(shape)
            {
                _promise = promise;
                _stage = stage;
                SetHandler(_stage.In, () =>
                {
                    var elem = Grab(_stage.In);
                    Channel.BasicPublish(Exchange,
                        RoutingKey,
                        elem.Mandatory,
                        elem.Properties,
                        elem.Bytes.ToArray());
                    Pull(_stage.In);
                },onUpstreamFinish: () =>
                {
                    _promise.SetResult(Done.Instance);
                }, onUpstreamFailure: ex =>
                {
                    _promise.SetException(ex);
                });
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
                Channel.ModelShutdown -= OnChannelShutdown;
                base.PostStop();//don't forget to call the base.PostStop()
            }
        }
    }
}
