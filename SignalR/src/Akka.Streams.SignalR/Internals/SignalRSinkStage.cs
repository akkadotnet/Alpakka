using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using Microsoft.AspNet.SignalR;
using AsyncCallback = Akka.Streams.Stage.AsyncCallback;

namespace Akka.Streams.SignalR.Internals
{
    internal sealed class SignalRSinkStage : GraphStage<SinkShape<Result>>
    {
        private readonly StreamConnection connection;
        private readonly Inlet<Result> inlet = new Inlet<Result>("signalr.in");

        public SignalRSinkStage(StreamConnection connection)
        {
            this.connection = connection;
            this.Shape = new SinkShape<Result>(inlet);
        }

        public override SinkShape<Result> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly SignalRSinkStage stage;
            private Task currentSend = Task.FromResult(0);

            public Logic(SignalRSinkStage stage) : base(stage.Shape)
            {
                this.stage = stage;
                SetHandler(stage.inlet, this);
            }

            public override void OnPush()
            {
                var element = Grab(stage.inlet);
                currentSend = stage.connection.Connection.Send(element.ToConnectionMessage());
                currentSend.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        FailStage(task.Exception);
                    }
                    else if (task.IsCanceled)
                    {
                        CompleteStage();
                    }
                    else
                    {
                        Pull(stage.inlet);
                    }
                });
            }

            public override void OnUpstreamFinish()
            {
                base.OnUpstreamFinish();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                base.OnUpstreamFailure(e);
            }
        }

        #endregion
    }
}
