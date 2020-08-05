using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using Microsoft.AspNet.SignalR;
using AsyncCallback = Akka.Streams.Stage.AsyncCallback;

namespace Akka.Streams.SignalR.Internals
{
    internal sealed class SignalRSinkStage : GraphStage<SinkShape<ISignalRResult>>
    {
        private readonly StreamConnection connection;
        private readonly ConnectionSinkSettings settings;
        private readonly Inlet<ISignalRResult> inlet = new Inlet<ISignalRResult>("signalr.in");

        public SignalRSinkStage(StreamConnection connection, ConnectionSinkSettings settings)
        {
            this.connection = connection;
            this.settings = settings;
            this.Shape = new SinkShape<ISignalRResult>(inlet);
        }

        public override SinkShape<ISignalRResult> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly SignalRSinkStage stage;
            private readonly Action onSuccess;
            private readonly Action<Exception> onFailure;

            private bool isShutdownInProgress = false;
            private bool isOperationInProgress = false;

            public Logic(SignalRSinkStage stage) : base(stage.Shape)
            {
                this.stage = stage;
                this.onSuccess = GetAsyncCallback(() =>
                {
                    isOperationInProgress = false;
                    TryShutdown();
                    TryPull(stage.inlet);
                });
                this.onFailure = GetAsyncCallback<Exception>(cause =>
                {
                    isShutdownInProgress = false;
                    FailStage(cause);
                });

                SetHandler(stage.inlet, this);
            }

            public override void OnPush()
            {
                var element = Grab(stage.inlet);
                isOperationInProgress = true;

                var send = element as Send;
                if (send != null)
                {
                    stage.connection.Connection
                        .Send(send.ToConnectionMessage())
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted || task.IsCanceled)
                                onFailure(task.Exception);
                            else
                                onSuccess();
                        });
                }
                else
                {
                    var broadcast = (Broadcast) element;
                    stage.connection.Connection
                        .Broadcast(broadcast.Data, broadcast.Excluded)
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted || task.IsCanceled)
                                onFailure(task.Exception);
                            else
                                onSuccess();
                        });
                }
            }

            private void TryShutdown()
            {
                if (isShutdownInProgress && !isOperationInProgress)
                {
                    CompleteStage();
                }
            }

            public override void PreStart()
            {
                SetKeepGoing(true);
                Pull(stage.inlet);
            }

            public override void OnUpstreamFinish()
            {
                isShutdownInProgress = true;
                TryShutdown();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                isShutdownInProgress = false;
                FailStage(e);
            }
        }

        #endregion
    }
}
