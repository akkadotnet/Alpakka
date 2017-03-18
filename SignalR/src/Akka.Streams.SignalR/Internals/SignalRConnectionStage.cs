using Akka.Streams.Stage;
using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR.Internals
{
    internal enum Role
    {
        Source,
        Sink
    }

    internal sealed class SignalRConnectionStage<T> : GraphStage<SinkShape<T>>
    {
        private readonly StreamConnection connection;
        private readonly Role role;
        private readonly Inlet<T> inlet = new Inlet<T>("signalr.in");
        public override SinkShape<T> Shape { get; }

        public SignalRConnectionStage(StreamConnection connection, Role role)
        {
            this.connection = connection;
            this.role = role;
            this.Shape = new SinkShape<T>(inlet);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => 
            new Logic(this);

        #region logic 

        private sealed class Logic : GraphStageLogic
        {
            private readonly SignalRConnectionStage<T> self;

            public Logic(SignalRConnectionStage<T> self) : base(self.Shape)
            {
                this.self = self;
                SetHandler(self.inlet, 
                    onPush: () =>
                    {
                        //TODO: onPush
                    },
                    onUpstreamFinish: () =>
                    {
                        //TODO: onUpstreamFinish
                    },
                    onUpstreamFailure: cause =>
                    {
                        //TODO: onUpstreamFailure
                    });
            }
        }

        #endregion
    }
}