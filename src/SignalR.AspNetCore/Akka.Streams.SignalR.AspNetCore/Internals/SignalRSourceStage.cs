using System;
using System.Collections.Generic;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.SignalR;

namespace Akka.Streams.SignalR.AspNetCore.Internals
{
    internal sealed class SignalRSourceStage : GraphStage<SourceShape<ISignalREvent>>
    {
        private readonly StreamConnector _connection;
        private readonly ConnectionSourceSettings _settings;
        private readonly Outlet<ISignalREvent> _outlet = new Outlet<ISignalREvent>("signalr.out");

        public SignalRSourceStage(StreamConnector connection, ConnectionSourceSettings settings)
        {
            _connection = connection;
            _settings = settings;
            Shape = new SourceShape<ISignalREvent>(_outlet);
        }

        public override SourceShape<ISignalREvent> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        #region logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly int _bufferCapacity;
            private readonly LinkedList<ISignalREvent> _buffer;
            private readonly Action<ISignalREvent> _onMessage;
            private readonly Action<ISignalREvent> _onOverflow;

            private SignalRSourceStage _stage;

            public Logic(SignalRSourceStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _bufferCapacity = stage._settings.BufferCapacity;
                _buffer = new LinkedList<ISignalREvent>();
                _onMessage = GetAsyncCallback<ISignalREvent>(e =>
                {
                    if (IsAvailable(_stage._outlet))
                    {
                        Push(_stage._outlet, e);
                    }
                    else
                    {
                        if (_buffer.Count >= _bufferCapacity)
                            _onOverflow(e);
                        else
                            Enqueue(e);
                    }
                });

                _onOverflow = SetupOverflowStrategy(stage._settings.OverflowStrategy);

                SetHandler(stage._outlet, this);
            }

            private void Enqueue(ISignalREvent message) => _buffer.AddLast(message);

            private ISignalREvent Dequeue()
            {
                var element = _buffer.First.Value;
                _buffer.RemoveFirst();
                return element;
            }

            private void HandleReceived(object sender, ISignalREvent e)
            {
                _onMessage(e);
            }

            public override void OnPull()
            {
                if (_buffer.Count > 0)
                {
                    var element = Dequeue();
                    Push(_stage._outlet, element);
                }
            }

            public override void PreStart()
            {
                base.PreStart();
                _stage._connection.Events += HandleReceived;
            }

            public override void PostStop()
            {
                _stage._connection.Events -= HandleReceived;
                _buffer.Clear();
                base.PostStop();
            }

            private Action<ISignalREvent> SetupOverflowStrategy(OverflowStrategy overflowStrategy)
            {
                switch (overflowStrategy)
                {
                    case OverflowStrategy.DropHead:
                        return message =>
                        {
                            _buffer.RemoveFirst();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropTail:
                        return message =>
                        {
                            _buffer.RemoveLast();
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
                            _buffer.Clear();
                            Enqueue(message);
                        };
                    case OverflowStrategy.Fail:
                        return message =>
                        {
                            FailStage(new BufferOverflowException($"{_stage._outlet} buffer has been overflown"));
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