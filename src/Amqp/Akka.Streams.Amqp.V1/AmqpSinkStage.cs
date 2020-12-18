using Akka.Streams.Stage;
﻿using System;
using System.Collections.Generic;
using Amqp;
using System.Threading.Tasks;
using Amqp.Framing;

namespace Akka.Streams.Amqp.V1
{
    public sealed class AmqpSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task>
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

        public Inlet<T> In { get; }
        public override SinkShape<T> Shape { get; }
        public IAmqpSinkSettings<T> AmqpSourceSettings { get; }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Done>();
            var logic = new AmqpSinkStageLogic(this, promise, Shape);
            return new LogicAndMaterializedValue<Task>(logic, promise.Task);
        }

        public AmqpSinkStage(IAmqpSinkSettings<T> amqpSourceSettings)
        {
            In = new Inlet<T>("AmqpSink.in");
            Shape = new SinkShape<T>(In);
            AmqpSourceSettings = amqpSourceSettings;
        }

        private class AmqpSinkStageLogic : GraphStageLogic
        {
            private readonly AmqpSinkStage<T> _stage;
            private readonly TaskCompletionSource<Done> _promise;
            private readonly Action<(IAmqpObject, Error)> _disconnectedCallback;

            private SenderLink _sender;

            public AmqpSinkStageLogic(AmqpSinkStage<T> amqpSinkStage, TaskCompletionSource<Done> promise, SinkShape<T> shape) : base(shape)
            {
                _stage = amqpSinkStage;
                _promise = promise;
                _disconnectedCallback = GetAsyncCallback<(IAmqpObject, Error)>(HandleDisconnection);

                SetHandler(
                    inlet: _stage.In, 
                    onPush: () =>
                    {
                        var elem = Grab(_stage.In);
                        _sender.Send(new Message(amqpSinkStage.AmqpSourceSettings.GetBytes(elem)));
                        Pull(_stage.In);
                    },
                    onUpstreamFinish: () => _promise.SetResult(Done.Instance),
                    onUpstreamFailure: _promise.SetException
                );
            }

            private async Task Connect()
            {
                var retry = 7;
                var exceptions = new List<Exception>();
                while (true)
                {
                    try
                    {
                        _sender = _stage.AmqpSourceSettings.GetSenderLink();
                        _sender.AddClosedCallback((sender, error) => _disconnectedCallback((sender, error)));
                        Log.Info("Connected to AMQP.V1 server.");
                        return;
                    }
                    catch (Exception e)
                    {
                        retry--;
                        if (retry == 0)
                            throw new AggregateException(exceptions);

                        exceptions.Add(e);
                        Log.Error($"[{retry}] more retries to connect to AMQP.V1 server.");
                        await Task.Delay(RetryInterval[retry]);
                    }
                }
            }

            private void HandleDisconnection((IAmqpObject sender, Error error) args)
                => FailStage(new DisconnectedException(args.sender, args.error));

            public override void PreStart()
            {
                base.PreStart();

                Connect().Wait();
                Pull(_stage.In);
            }

            public override void PostStop()
            {
                _sender?.Close();
                _sender?.Session?.Close();
                _sender?.Session?.Connection?.Close();
                base.PostStop();
            }
        }
    }
}
