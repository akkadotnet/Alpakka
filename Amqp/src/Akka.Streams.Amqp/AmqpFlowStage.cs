using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using RabbitMQ.Client;

namespace Akka.Streams.Amqp
{
    /// <summary>
    /// This stage materializes to a <see cref="T:System.Threading.Tasks.Task" />
    /// </summary>
    /// <inheritdoc />
    public class AmqpFlowStage<TPassThrough> : GraphStageWithMaterializedValue<FlowShape<(OutgoingMessage, TPassThrough), TPassThrough>, Task>
    {
        public static readonly Attributes DefaultAttributes =
            Attributes.CreateName("AmqpFlow").And(ActorAttributes.CreateDispatcher("akka.stream.default-blocking-io-dispatcher"));

        public AmqpSinkSettings Settings { get; }
        public readonly Inlet<(OutgoingMessage, TPassThrough)> In = new Inlet<(OutgoingMessage, TPassThrough)>("AmqpFlow.in");
        public readonly Outlet<TPassThrough> Out = new Outlet<TPassThrough>("AmqpFlow.out");

        public AmqpFlowStage(AmqpSinkSettings settings)
        {
            Settings = settings;
            Shape = new FlowShape<(OutgoingMessage, TPassThrough), TPassThrough>(In, Out);
        }

        public override FlowShape<(OutgoingMessage, TPassThrough), TPassThrough> Shape { get; }
        protected override Attributes InitialAttributes => DefaultAttributes;

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new Logic(this, promise);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        public override string ToString() => "AmqpFlow";

        private class Logic : AmqpConnectorLogic
        {
            private readonly AmqpFlowStage<TPassThrough> _stage;
            private Action<ShutdownEventArgs> _shutdownCallback;
            private readonly TaskCompletionSource<Done> _promise;

            public Logic(AmqpFlowStage<TPassThrough> stage, TaskCompletionSource<Done> promise) : base(stage.Shape)
            {
                _promise = promise;
                _stage = stage;

                SetHandler(_stage.Out, onPull: () => Pull(_stage.In));

                SetHandler(_stage.In,
                    onPush: () =>
                    {
                        var (elem, passThrough) = Grab(_stage.In);
                        Channel.BasicPublish(
                            Exchange,
                            elem.RoutingKey ?? RoutingKey,
                            elem.Mandatory,
                            elem.Properties,
                            elem.Bytes.ToArray());

                        Push(_stage.Out, passThrough);
                        //Pull(_stage.In);
                    },
                    onUpstreamFinish: () => _promise.TrySetResult(Done.Instance),
                    onUpstreamFailure: ex => _promise.TrySetException(ex));
            }

            public string Exchange => _stage.Settings.Exchange ?? "";

            public string RoutingKey => _stage.Settings.RoutingKey ?? "";

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
                    _promise.SetException(exception);
                    FailStage(exception);
                });

                Channel.ModelShutdown += OnChannelShutdown;
                Pull(_stage.In);
            }

            private void OnChannelShutdown(object sender, ShutdownEventArgs shutdownEventArgs) => _shutdownCallback?.Invoke(shutdownEventArgs);

            public override void PostStop()
            {
                _promise.TrySetException(new ApplicationException("stage stopped unexpectedly"));
                if (Channel != null)
                    Channel.ModelShutdown -= OnChannelShutdown;
                base.PostStop();
            }

            public override void OnFailure(Exception ex) => _promise.TrySetException(ex);
        }
    }
}