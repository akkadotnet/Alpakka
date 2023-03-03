using Akka.Event;
using Akka.Streams.Amqp.V1.Util;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Amqp.V1.Internal;
using Error = Amqp.Framing.Error;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSourceStage<T> : GraphStage<SourceShape<T>>
    {
        private static readonly Dictionary<int, TimeSpan> RetryInterval =
            new Dictionary<int, TimeSpan>()
            {
                { 6, TimeSpan.FromMilliseconds(100) },
                { 5, TimeSpan.FromMilliseconds(500) },
                { 4, TimeSpan.FromMilliseconds(1000) },
                { 3, TimeSpan.FromMilliseconds(2000) },
                { 2, TimeSpan.FromMilliseconds(4000) },
                { 1, TimeSpan.FromMilliseconds(8000) },
            };

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
            private readonly AmqpSourceStage<T> _stage;
            private readonly Outlet<T> _outlet;
            private readonly IAmqpSourceSettings<T> _amqpSourceSettings;
            private readonly Decider _decider;
            private readonly Queue<Message> _queue = new Queue<Message>();
            private readonly Action<(IAmqpObject, Error)> _disconnectedCallback;
            private readonly Action<Message> _consumerCallback;

            private ReceiverLink _receiver;

            public AmqpSourceStageLogic(AmqpSourceStage<T> stage, Attributes attributes) : base(stage.Shape)
            {
                _stage = stage;
                _outlet = stage.Out;
                _amqpSourceSettings = stage.AmqpSourceSettings;
                _decider = attributes.GetDeciderOrDefault();
                _disconnectedCallback = GetAsyncCallback<(IAmqpObject, Error)>(HandleDisconnection);
                _consumerCallback = GetAsyncCallback<Message>(HandleDelivery);

                SetHandler(_outlet, () =>
                {
                    if (_queue.TryDequeue(out var msg))
                    {
                        PushMessage(msg);
                    }
                }, onDownstreamFinish: ex => CompleteStage());
            }

            private void HandleConnectionResult(ConnectResult result)
            {
                if (!result.IsSuccessful)
                {
                    FailStage(result.Exception);
                    return;
                }
                Log.Info("Connected to AMQP.V1 server.");
            }

            public override void PreStart()
            {
                base.PreStart();

                Task.Factory.StartNew(() =>
                {
                    var callback = GetAsyncCallback<ConnectResult>(HandleConnectionResult);
                    Connect().ContinueWith(result =>
                    {
                        if (result.Exception != null)
                            callback(new ConnectResult(result.Exception));
                        callback(result.Result);
                    }).Wait();
                }, TaskCreationOptions.RunContinuationsAsynchronously | TaskCreationOptions.LongRunning);
            }

            private async Task<ConnectResult> Connect()
            {
                var retry = 7;
                var exceptions = new List<Exception>();
                while (true)
                {
                    try
                    {
                        _receiver = _stage.AmqpSourceSettings.GetReceiverLink();
                        _receiver.AddClosedCallback((sender, error) => _disconnectedCallback((sender, error)));
                        _receiver.Start(_amqpSourceSettings.Credit, (_, m) => _consumerCallback.Invoke(m));
                        return ConnectResult.Success;
                    }
                    catch (Exception e)
                    {
                        if (!_stage.AmqpSourceSettings.ManageConnection)
                        {
                            return new ConnectResult(new ConnectionException(
                                "Failed to connect to AMQP.V1 server. Could not retry connection because SourceSettings does not manage the Connection object.", e));
                        }

                        retry--;
                        if (retry == 0)
                            return new ConnectResult(new AggregateException("Failed to connect to AMQP.V1 server.", exceptions));

                        exceptions.Add(e);
                        Log.Error($"[{retry}] more retries to connect to AMQP.V1 server.");
                        await Task.Delay(RetryInterval[retry]);
                    }
                }
            }

            private void HandleDisconnection((IAmqpObject sender, Error error) args)
                => FailStage(new DisconnectedException(args.sender, args.error));

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
                _receiver?.Close();
                base.PostStop();
            }
        }
    }
}
