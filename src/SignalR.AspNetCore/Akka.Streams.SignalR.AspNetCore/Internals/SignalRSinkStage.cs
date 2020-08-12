using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.SignalR;
using AsyncCallback = Akka.Streams.Stage.AsyncCallback;

namespace Akka.Streams.SignalR.AspNetCore.Internals
{
    internal sealed class SignalRSinkStage : GraphStage<SinkShape<ISignalRResult>>
    {
        private readonly IHubClients<IClientProxy> _connection;
        private readonly ConnectionSinkSettings _settings;
        private readonly Inlet<ISignalRResult> _inlet = new Inlet<ISignalRResult>("signalr.in");

        public SignalRSinkStage(IHubClients<IClientProxy> connection, ConnectionSinkSettings settings)
        {
            _connection = connection;
            _settings = settings;
            Shape = new SinkShape<ISignalRResult>(_inlet);
        }

        public override SinkShape<ISignalRResult> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly SignalRSinkStage _stage;
            private readonly Action _onSuccess;
            private readonly Action<Exception> _onFailure;

            private bool _isShutdownInProgress = false;
            private bool _isOperationInProgress = false;

            public Logic(SignalRSinkStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _onSuccess = GetAsyncCallback(() =>
                {
                    _isOperationInProgress = false;
                    TryShutdown();
                    TryPull(stage._inlet);
                });
                _onFailure = GetAsyncCallback<Exception>(cause =>
                {
                    _isShutdownInProgress = false;
                    FailStage(cause);
                });

                SetHandler(stage._inlet, this);
            }

            public override void OnPush()
            {
                var element = Grab(_stage._inlet);
                _isOperationInProgress = true;

                if (element is Send send)
                {
                    IClientProxy target;
                    if (send.Group == null)
                        target = _stage._connection.Client(send.ConnectionId);
                    else {
                        target = _stage._connection.GroupExcept(send.Group, send.ExcludedConnectionIds.ToList());
                    }
                
                    target
                        .SendAsync(nameof(IClientSink.Receive), send.Data)
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted || task.IsCanceled)
                                _onFailure(task.Exception);
                            else
                                _onSuccess();
                        })
                        .Wait();
                }
                else
                {
                    var broadcast = (Broadcast) element;
                    _stage._connection.AllExcept(broadcast.ExcludedConnectionIds)
                        .SendAsync(nameof(IClientSink.Receive), broadcast.Data)
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted || task.IsCanceled)
                                _onFailure(task.Exception);
                            else
                                _onSuccess();
                        })
                        .Wait();
                }
            }

            private void TryShutdown()
            {
                if (_isShutdownInProgress && !_isOperationInProgress)
                {
                    CompleteStage();
                }
            }

            public override void PreStart()
            {
                SetKeepGoing(true);
                Pull(_stage._inlet);
            }

            public override void OnUpstreamFinish()
            {
                _isShutdownInProgress = true;
                TryShutdown();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _isShutdownInProgress = false;
                FailStage(e);
            }
        }

        #endregion
    }
}
