//-----------------------------------------------------------------------
// <copyright file="LogRotatorSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.IO;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.File
{
    public static class LogRotatorSink
    {
        /// <summary>
        /// Sink directing the incoming <see cref="ByteString" />s to new files whenever `triggerGenerator` returns a value.
        /// </summary>
        /// <param name="triggerGeneratorCreator">Creates a function that triggers rotation by returning a value</param>
        /// <param name="fileOpenOptions">File options for file creation</param>
        public static Sink<ByteString, Task<Done>> Create(Func<ByteString, Option<string>> triggerGeneratorCreator, FileMode fileOpenOptions = FileMode.OpenOrCreate) =>
            Sink.FromGraph(new LogRotatorSink<ByteString, string, IOResult>(triggerGeneratorCreator, fileName => FileIO.ToFile(new FileInfo(fileName), fileOpenOptions)));

        /// <summary>
        /// Sink directing the incoming <see cref="ByteString"/>s to a new <seealso cref="Sink" /> created by `sinkFactory` whenever `triggerGenerator` returns a value.
        /// </summary>
        /// <param name="triggerGeneratorCreator">Creates a function that triggers rotation by returning a value</param>
        /// <param name="sinkFactory">Creates sinks for <seealso cref="ByteString" />s from the value returned by `triggerGenerator`</param>
        public static Sink<ByteString, Task<Done>> WithSinkFactory<TC, TR>(Func<ByteString, Option<TC>> triggerGeneratorCreator, Func<TC, Sink<ByteString, Task<TR>>> sinkFactory) =>
            Sink.FromGraph(new LogRotatorSink<ByteString, TC, TR>(triggerGeneratorCreator, sinkFactory));

        /// <summary>
        /// Sink directing the incoming <see cref="T"/>s to a new <seealso cref="Sink" /> created by `sinkFactory` whenever `triggerGenerator` returns a value.
        /// </summary>
        /// <param name="triggerGeneratorCreator">Creates a function that triggers rotation by returning a value</param>
        /// <param name="sinkFactory">Creates sinks for <see cref="T"/>s from the value returned by `triggerGenerator`</param>
        public static Sink<T, Task<Done>> WithTypedSinkFactory<T, TC, TR>(Func<T, Option<TC>> triggerGeneratorCreator, Func<TC, Sink<T, Task<TR>>> sinkFactory) =>
            Sink.FromGraph(new LogRotatorSink<T, TC, TR>(triggerGeneratorCreator, sinkFactory));
    }

    /// <summary>
    /// "Log Rotator Sink" graph stage
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="TC">Criterion type (for files a `string`)</typeparam>
    /// <typeparam name="TR">Result type in materialized tasks of `sinkFactory`</typeparam>
    internal class LogRotatorSink<T, TC, TR> : GraphStageWithMaterializedValue<SinkShape<T>, Task<Done>>
    {
        private readonly Func<T, Option<TC>> _triggerGeneratorCreator;
        private readonly Func<TC, Sink<T, Task<TR>>> _sinkFactory;

        private Inlet<T> In { get; } = new Inlet<T>("LogRotatorSink.In");
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// Creates an instance of the <see cref="LogRotatorSink"/>
        /// </summary>
        /// <param name="triggerGeneratorCreator">Creates a function that triggers rotation by returning a value</param>
        /// <param name="sinkFactory">Creates sinks for `ByteString`s from the value returned by `triggerGenerator`</param>
        public LogRotatorSink(Func<T, Option<TC>> triggerGeneratorCreator, Func<TC, Sink<T, Task<TR>>> sinkFactory)
        {
            _triggerGeneratorCreator = triggerGeneratorCreator;
            _sinkFactory = sinkFactory;

            Shape = new SinkShape<T>(In);
        }

        public override ILogicAndMaterializedValue<Task<Done>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<Done>();
            var logic = new LogRotatorSinkLogic(this, completion);
            return new LogicAndMaterializedValue<Task<Done>>(logic, completion.Task);
        }

        private sealed class LogRotatorSinkLogic : GraphStageLogic
        {
            private readonly LogRotatorSink<T, TC, TR> _sink;
            private readonly TaskCompletionSource<Done> _promise;
            private SubSourceOutlet<T> _sourceOut;
            private ImmutableList<Task<TR>> _sinkCompletions = ImmutableList<Task<TR>>.Empty;
            private bool _isFinishing;

            private Try<TR> NotYetThere { get; } = new Try<TR>(new Exception());

            public LogRotatorSinkLogic(LogRotatorSink<T, TC, TR> sink, TaskCompletionSource<Done> promise)
                : base(sink.Shape)
            {
                _sink = sink;
                _promise = promise;

                SetHandler(sink.In,
                    onPush: () =>
                    {
                        var data = Grab(sink.In);
                        var trigger = CheckTrigger(data);
                        if (trigger.HasValue)
                            Rotate(trigger.Value, data);
                        else
                            if (!IsClosed(sink.In)) Pull(sink.In);
                    },
                    onUpstreamFinish: () =>
                    {
                        promise.TrySetResult(Done.Instance);
                        CompleteStage();
                    },
                    onUpstreamFailure: FailThisStage);
            }

            private void FailThisStage(Exception ex)
            {
                if (_promise.Task.IsCompleted)
                    return;

                _sourceOut?.Fail(ex);
                Cancel(_sink.In);
                _promise.SetException(ex);
            }

            private void CompleteThisStage()
            {
                _sourceOut?.Complete();
                Task.WhenAll(_sinkCompletions).ContinueWith(t => _promise.SetResult(Done.Instance));
            }

            private Option<TC> CheckTrigger(T data)
            {
                try
                {
                    return _sink._triggerGeneratorCreator(data);
                }
                catch (Exception ex)
                {
                    FailThisStage(ex);
                    return Option<TC>.None;
                }
            }

            private void SinkCompletionCallbackHandler(Task<TR> future, Holder<TR> h)
            {
                switch (h.Element)
                {
                    case var r when r.IsSuccess && _sinkCompletions.Count == 1 && _sinkCompletions.Head() == future:
                        _promise.TrySetResult(Done.Instance);
                        CompleteStage();
                        break;
                    case var r when r.IsSuccess:
                        _sinkCompletions = _sinkCompletions.RemoveAll(t => t != future);
                        break;
                    case var r:
                        FailThisStage(r.Failure.Value);
                        break;
                }
            }

            public override void PreStart()
            {
                base.PreStart();
                // we must pull the first element cos we are a sink
                Pull(_sink.In);
            }

            public override void PostStop()
            {
                base.PostStop();
                Task.WhenAll(_sinkCompletions).ContinueWith(t => _promise.TrySetResult(Done.Instance));
            }

            private Action<Holder<TR>> FutureCb(Task<TR> newFuture) =>
                GetAsyncCallback(((Func<Task<TR>, Action<Holder<TR>>>)(b => c => SinkCompletionCallbackHandler(b, c)))(newFuture));

            /// <summary>
            /// We recreate the tail of the stream, and emit the data for the next req
            /// </summary>
            private void Rotate(TC triggerValue, T data)
            {
                var prevOut = new Option<SubSourceOutlet<T>>(_sourceOut);

                _sourceOut = new SubSourceOutlet<T>(this, "LogRotatorSink.Sub-Out");
                _sourceOut.SetHandler(new LambdaOutHandler(onPull: () =>
                {
                    _sourceOut.Push(data);
                    SwitchToNormalMode();
                }));

                SetHandler(_sink.In,
                    onPush: () => Log.Info("No push should happen while we are waiting for the sub-stream to grab the dangling data!"),
                    onUpstreamFinish: () =>
                    {
                        SetKeepGoing(true);
                        _isFinishing = true;
                    },
                    onUpstreamFailure: FailThisStage);

                var newFuture = Source.FromGraph(_sourceOut.Source)
                    .RunWith(_sink._sinkFactory(triggerValue), Materializer);

                _sinkCompletions = _sinkCompletions.Add(newFuture);

                var holder = new Holder<TR>(NotYetThere, FutureCb(newFuture));

                newFuture.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var exception = t.Exception?.InnerExceptions != null && t.Exception.InnerExceptions.Count == 1
                            ? t.Exception.InnerExceptions[0]
                            : t.Exception;

                        holder.Invoke(new Try<TR>(exception));
                    }
                    else
                    {
                        holder.Invoke(new Try<TR>(t.Result));
                    }
                }, TaskContinuationOptions.NotOnCanceled);

                prevOut.Value?.Complete();
            }

            /// <summary>
            /// We change path if needed or push the grabbed data
            /// </summary>
            private void SwitchToNormalMode()
            {
                if (_isFinishing)
                {
                    CompleteThisStage();
                    return;
                }

                SetHandler(_sink.In,
                    onPush: () =>
                    {
                        var data = Grab(_sink.In);
                        var trigger = CheckTrigger(data);
                        if (trigger.HasValue)
                            Rotate(trigger.Value, data);
                        else
                            _sourceOut.Push(data);
                    },
                    onUpstreamFinish: CompleteThisStage,
                    onUpstreamFailure: FailThisStage);

                _sourceOut.SetHandler(new LambdaOutHandler(() => Pull(_sink.In)));
            }

            private sealed class Holder<TIn>
            {
                private readonly Action<Holder<TIn>> _callback;

                public Holder(Try<TIn> element, Action<Holder<TIn>> callback)
                {
                    _callback = callback;
                    Element = element;
                }

                public Try<TIn> Element { get; private set; }

                private void SetElement(Try<TIn> result)
                {
                    Element = result.IsSuccess && !result.Success.HasValue
                        ? new Try<TIn>(ReactiveStreamsCompliance.ElementMustNotBeNullException)
                        : result;
                }

                public void Invoke(Try<TIn> result)
                {
                    SetElement(result);
                    _callback(this);
                }
            }
        }
    }
}
