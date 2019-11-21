using System;
using System.Collections.Generic;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.SignalR;

namespace Akka.Streams.SignalR.AspNetCore.Internals
{
    internal sealed class SignalRSourceStage : GraphStage<SourceShape<ISignalREvent>>
    {
        private readonly StreamConnector connection;
        private readonly ConnectionSourceSettings settings;
        private readonly Outlet<ISignalREvent> outlet = new Outlet<ISignalREvent>("signalr.out");

        public SignalRSourceStage(StreamConnector connection, ConnectionSourceSettings settings)
        {
            this.connection = connection;
            this.settings = settings;
            this.Shape = new SourceShape<ISignalREvent>(outlet);
        }

        public override SourceShape<ISignalREvent> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly int bufferCapacity;
            private readonly LinkedList<ISignalREvent> buffer;
            private readonly Action<ISignalREvent> onMessage;
            private readonly Action<ISignalREvent> onOverflow;

            private SignalRSourceStage stage;

            public Logic(SignalRSourceStage stage) : base(stage.Shape)
            {
                this.stage = stage;
                this.bufferCapacity = stage.settings.BufferCapacity;
                this.buffer = new LinkedList<ISignalREvent>();
                this.onMessage = GetAsyncCallback<ISignalREvent>(e =>
                {
                    if (IsAvailable(this.stage.outlet))
                    {
                        Push(this.stage.outlet, e);
                    }
                    else
                    {
                        if (this.buffer.Count >= this.bufferCapacity)
                            this.onOverflow(e);
                        else
                            Enqueue(e);
                    }
                });

                this.onOverflow = SetupOverflowStrategy(stage.settings.OverflowStrategy);

                SetHandler(stage.outlet, this);
            }

            private void Enqueue(ISignalREvent message) => buffer.AddLast(message);

            private ISignalREvent Dequeue()
            {
                var element = buffer.First.Value;
                buffer.RemoveFirst();
                return element;
            }

            private void HandleReceived(object sender, ISignalREvent e)
            {
                this.onMessage(e);
            }

            public override void OnPull()
            {
                if (buffer.Count > 0)
                {
                    var element = Dequeue();
                    Push(stage.outlet, element);
                }
            }

            public override void PreStart()
            {
                base.PreStart();
                stage.connection.Events += HandleReceived;
            }

            public override void PostStop()
            {
                stage.connection.Events -= HandleReceived;
                buffer.Clear();
                base.PostStop();
            }

            private Action<ISignalREvent> SetupOverflowStrategy(OverflowStrategy overflowStrategy)
            {
                switch (overflowStrategy)
                {
                    case OverflowStrategy.DropHead:
                        return message =>
                        {
                            buffer.RemoveFirst();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropTail:
                        return message =>
                        {
                            buffer.RemoveLast();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropNew:
                        return message =>
                        {
                            // do nothing
                        };
                    case OverflowStrategy.DropBuffer:
                        return message =>
                        {
                            buffer.Clear();
                            Enqueue(message);
                        };
                    case OverflowStrategy.Fail:
                        return message =>
                        {
                            FailStage(new BufferOverflowException($"{stage.outlet} buffer has been overflown"));
                        };
                    case OverflowStrategy.Backpressure:
                        return message =>
                        {
                            throw new NotSupportedException("OverflowStrategy.Backpressure is not supported");
                        };
                    default: throw new NotSupportedException($"Unknown option: {overflowStrategy}");
                }
            }
        }

        #endregion
    }
}