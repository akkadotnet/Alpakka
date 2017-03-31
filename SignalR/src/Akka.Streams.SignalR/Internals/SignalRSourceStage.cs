using System;
using System.Collections.Generic;
using Akka.Streams.Stage;

namespace Akka.Streams.SignalR.Internals
{
    internal sealed class SignalRSourceStage : GraphStage<SourceShape<ReceivedMessage>>
    {
        private readonly StreamConnection connection;
        private readonly ConnectionSourceSettings settings;
        private readonly Outlet<ReceivedMessage> outlet = new Outlet<ReceivedMessage>("signalr.out");

        public SignalRSourceStage(StreamConnection connection, ConnectionSourceSettings settings)
        {
            this.connection = connection;
            this.settings = settings;
            this.Shape = new SourceShape<ReceivedMessage>(outlet);
        }

        public override SourceShape<ReceivedMessage> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly int bufferCapacity;
            private readonly LinkedList<ReceivedMessage> buffer;
            private readonly Action<ReceivedMessage> onMessage;
            private readonly Action<ReceivedMessage> onOverflow;

            private SignalRSourceStage stage;

            public Logic(SignalRSourceStage stage) : base(stage.Shape)
            {
                this.stage = stage;
                this.bufferCapacity = stage.settings.BufferCapacity;
                this.buffer = new LinkedList<ReceivedMessage>();
                this.onMessage = GetAsyncCallback<ReceivedMessage>(e =>
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

            private void Enqueue(ReceivedMessage message) => buffer.AddLast(message);

            private ReceivedMessage Dequeue()
            {
                var element = buffer.First.Value;
                buffer.RemoveFirst();
                return element;
            }

            private void HandleReceived(object sender, ReceivedMessage e)
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
                stage.connection.Received += HandleReceived;
            }

            public override void PostStop()
            {
                stage.connection.Received -= HandleReceived;
                buffer.Clear();
                base.PostStop();
            }

            private Action<ReceivedMessage> SetupOverflowStrategy(OverflowStrategy overflowStrategy)
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