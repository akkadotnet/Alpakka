#region copyright
// -----------------------------------------------------------------------
//  <copyright file="ChannelReaderSource.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams.Channels.Internal
{
    internal sealed class ChannelReaderSource<T> : GraphStage<SourceShape<T>>
    {
        #region logic

        sealed class Logic : OutGraphStageLogic
        {
            private readonly Outlet<T> _outlet;
            private readonly ChannelReader<T> _reader;
            private readonly Action<bool> _onValueRead;
            private readonly Action<Exception> _onValueReadFailure;
            private readonly Action<Exception> _onReaderComplete;

            public Logic(ChannelReaderSource<T> source) : base(source.Shape)
            {
                _outlet = source.Outlet;
                _reader = source._reader;
                _onValueRead = GetAsyncCallback<bool>(OnValueRead);
                _onValueReadFailure = GetAsyncCallback<Exception>(OnValueReadFailure);
                _onReaderComplete = GetAsyncCallback<Exception>(OnReaderComplete);

                _reader.Completion.ContinueWith(t =>
                {
                    if (t.IsFaulted) _onReaderComplete(t.Exception);
                    else if (t.IsCanceled) _onReaderComplete(new TaskCanceledException(t));
                    else _onReaderComplete(null);
                });

                SetHandler(_outlet, this);
            }

            private void OnReaderComplete(Exception reason)
            {
                if (reason is null)
                    CompleteStage();
                else
                    FailStage(reason);
            }

            private void OnValueReadFailure(Exception reason) => FailStage(reason);

            private void OnValueRead(bool dataAvailable)
            {
                if (dataAvailable && _reader.TryRead(out var element))
                    Push(_outlet, element);
                else
                    CompleteStage();
            }

            public override void OnPull()
            {
                if (_reader.TryRead(out var element))
                {
                    Push(_outlet, element);
                }
                else
                {
                    var continuation = _reader.WaitToReadAsync();
                    if (continuation.IsCompletedSuccessfully)
                    {
                        var dataAvailable = continuation.GetAwaiter().GetResult();
                        if (dataAvailable && _reader.TryRead(out element))
                            Push(_outlet, element);
                        else
                            CompleteStage();
                    }
                    else
                    {
                        var task = continuation.AsTask();
                        if (!continuation.IsCompleted)
                            task.ContinueWith(t =>
                            {
                                if (t.IsFaulted) _onValueReadFailure(t.Exception);
                                else if (t.IsCanceled) _onValueReadFailure(new TaskCanceledException(t));
                                else _onValueRead(t.Result);
                            });
                        else if (continuation.IsFaulted)
                            FailStage(task.Exception);
                        else
                            FailStage(new TaskCanceledException(task));
                    }
                }
            }
        }

        #endregion

        private readonly ChannelReader<T> _reader;

        public ChannelReaderSource(ChannelReader<T> reader)
        {
            _reader = reader;
            Outlet = new Outlet<T>("channelReader.out");
            Shape = new SourceShape<T>(Outlet);
        }

        public Outlet<T> Outlet { get; }
        public override SourceShape<T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}