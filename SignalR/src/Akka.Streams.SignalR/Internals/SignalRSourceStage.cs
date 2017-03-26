using Akka.Streams.Stage;

namespace Akka.Streams.SignalR.Internals
{
    internal sealed class SignalRSourceStage : GraphStage<SourceShape<ReceivedMessage>>
    {
        private readonly StreamConnection connection;
        private readonly Outlet<ReceivedMessage> outlet = new Outlet<ReceivedMessage>("signalr.out");
        private readonly IBuffer<ReceivedMessage> buffer;

        public SignalRSourceStage(StreamConnection connection, IBuffer<ReceivedMessage> buffer)
        {
            this.connection = connection;
            this.buffer = buffer;
            this.Shape = new SourceShape<ReceivedMessage>(outlet);
        }

        public override SourceShape<ReceivedMessage> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : OutGraphStageLogic
        {
            private SignalRSourceStage stage;
            private bool isUpstreamWaiting = false;

            public Logic(SignalRSourceStage stage) : base(stage.Shape)
            {
                this.stage = stage;

                SetHandler(stage.outlet, this);
            }
            
            public override void OnPull()
            {
                ReceivedMessage msg;
                if (stage.buffer.TryDequeue(out msg))
                {
                    isUpstreamWaiting = false;
                    Push(stage.outlet, msg);
                }
                else
                {
                    isUpstreamWaiting = true;
                }
            }

            public override void OnDownstreamFinish()
            {
                if (!IsClosed(stage.outlet))
                {
                    Complete(stage.outlet);    
                }

                base.OnDownstreamFinish();
            }
        }

        #endregion
    }
}