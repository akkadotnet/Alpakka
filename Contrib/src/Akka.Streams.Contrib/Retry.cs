using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Contrib
{
    public static class Retry
    {
        public static IGraph<FlowShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>>, TM> Create<TI, TS, TO, TM>(
            IGraph<FlowShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>>, TM> flow, Func<TS, Tuple<TI, TS>> retryWith)
        {
            return GraphDsl.Create(flow, (b, origFlow) =>
            {
                var retry = b.Add(new RetryCoordinator<TI, TS, TO>(retryWith));

                b.From(retry.Outlet2).Via(origFlow).To(retry.Inlet2);

                return new FlowShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>>(retry.Inlet1, retry.Outlet1);
            });
        }

        public static IGraph<FlowShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>>, TM> Concat<TI, TS, TO, TM>(long limit,
            IGraph<FlowShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>>, TM> flow, Func<TS, IEnumerable<Tuple<TI, TS>>> retryWith)
        {
            return GraphDsl.Create(flow, (b, origFlow) =>
            {
                var retry = b.Add(new RetryConcatCoordinator<TI, TS, TO>(limit, retryWith));

                b.From(retry.Outlet2).Via(origFlow).To(retry.Inlet2);

                return new FlowShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>>(retry.Inlet1, retry.Outlet1);
            });
        }


        private class RetryCoordinator<TI, TS, TO> : GraphStage<BidiShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>, Tuple<Result<TO>, TS>, Tuple<TI, TS>>>
        {
            #region Logic

            private sealed class Logic : GraphStageLogic
            {
                private readonly RetryCoordinator<TI, TS, TO> _retry;
                private bool _elementInCycle;
                private Tuple<TI, TS> _pending;

                public Logic(RetryCoordinator<TI, TS, TO> retry) : base(retry.Shape)
                {

                    _retry = retry;

                    SetHandler(retry.In1, onPush: () =>
                    {
                        var item = Grab(retry.In1);
                        if (!HasBeenPulled(retry.In2))
                            Pull(retry.In2);
                        Push(retry.Out2, item);
                        _elementInCycle = true;
                    }, onUpstreamFinish: () =>
                    {
                        if (!_elementInCycle)
                            CompleteStage();
                    });

                    SetHandler(retry.Out1, onPull: () =>
                    {
                        if (IsAvailable(retry.Out2))
                            Pull(retry.In1);
                        else
                            Pull(retry.In2);
                    });

                    SetHandler(retry.In2, onPush: () =>
                    {
                        _elementInCycle = false;
                        var t = Grab(retry.In2);
                        var result = t.Item1;

                        if (result.IsSuccess)
                            PushAndCompleteIfLast(t);
                        else
                        {
                            var r = retry._retryWith(t.Item2);
                            if (r == null)
                                PushAndCompleteIfLast(t);
                            else
                            {
                                Pull(retry.In2);
                                if (IsAvailable(retry.Out2))
                                {
                                    Push(retry.Out2, r.Item2);
                                    _elementInCycle = true;
                                }
                                else
                                    _pending = r;
                            }

                        }
                    });

                    SetHandler(retry.Out2, onPull: () =>
                    {
                        if (IsAvailable(retry.Out1) && !_elementInCycle)
                        {
                            if (_pending != null)
                            {
                                Push(retry.Out2, _pending);
                                _pending = null;
                                _elementInCycle = true;
                            }
                            else if (!HasBeenPulled(retry.In1))
                                Pull(retry.In1);
                        }
                    }, onDownstreamFinish: () =>
                    {
                        //Do Nothing, intercept completion as downstream
                    });
                }

                private void PushAndCompleteIfLast(Tuple<Result<TO>, TS> item)
                {
                    Push(_retry.Out1, item);
                    if (IsClosed(_retry.In1))
                        CompleteStage();
                }
            }

            #endregion

            private readonly Func<TS, Tuple<TI, TS>> _retryWith;

            public RetryCoordinator(Func<TS, Tuple<TI, TS>> retryWith)
            {
                _retryWith = retryWith;

                In1 = new Inlet<Tuple<TI, TS>>("Retry.ext.in");
                Out1 = new Outlet<Tuple<Result<TO>, TS>>("Retry.ext.out");
                In2 = new Inlet<Tuple<Result<TO>, TS>>("Retry.int.in");
                Out2 = new Outlet<Tuple<TI, TS>>("Retry.int.out");
                Shape = new BidiShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>, Tuple<Result<TO>, TS>, Tuple<TI, TS>>(In1, Out1, In2, Out2);
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override BidiShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>, Tuple<Result<TO>, TS>, Tuple<TI, TS>> Shape { get; }

            public Inlet<Tuple<TI, TS>> In1 { get; }
            public Outlet<Tuple<Result<TO>, TS>> Out1 { get; }
            public Inlet<Tuple<Result<TO>, TS>> In2 { get; }
            public Outlet<Tuple<TI, TS>> Out2 { get; }
        }


        private class RetryConcatCoordinator<TI, TS, TO> : GraphStage<BidiShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>, Tuple<Result<TO>, TS>, Tuple<TI, TS>>>
        {
            #region Logic

            private sealed class Logic : GraphStageLogic
            {
                private readonly RetryConcatCoordinator<TI, TS, TO> _retry;
                private readonly Queue<Tuple<TI, TS>> _queue = new Queue<Tuple<TI, TS>>();
                private bool _elementInCycle;

                public Logic(RetryConcatCoordinator<TI, TS, TO> retry) : base(retry.Shape)
                {
                    _retry = retry;

                    SetHandler(retry.In1, onPush: () =>
                    {
                        var item = Grab(retry.In1);
                        if (!HasBeenPulled(retry.In2))
                            Pull(retry.In2);
                        if (IsAvailable(retry.Out2))
                        {
                            Push(retry.Out2, item);
                            _elementInCycle = true;
                        }
                        else
                            _queue.Enqueue(item);
                    }, onUpstreamFinish: () =>
                    {
                        if (!_elementInCycle && _queue.Count == 0)
                            CompleteStage();
                    });

                    SetHandler(retry.Out1, onPull: () =>
                    {
                        if (_queue.Count == 0)
                        {
                            if (IsAvailable(retry.Out2))
                                Pull(retry.In1);
                            else
                                Pull(retry.In2);
                        }
                        else
                        {
                            Pull(retry.In2);
                            if (IsAvailable(retry.Out2))
                            {
                                Push(retry.Out2, _queue.Dequeue());
                                _elementInCycle = true;
                            }
                        }
                    });

                    SetHandler(retry.In2, onPush: () =>
                    {
                        _elementInCycle = false;
                        var t = Grab(retry.In2);
                        var result = t.Item1;

                        if (result.IsSuccess)
                            PushAndCompleteIfLast(t);
                        else
                        {
                            var r = retry._retryWith(t.Item2);
                            if (r == null)
                                PushAndCompleteIfLast(t);
                            else
                            {
                                var items = r.ToList();
                                if (items.Count + _queue.Count > retry._limit)
                                    FailStage(new IllegalStateException($"Queue limit of {retry._limit} has been exceeded. Trying to append {items.Count} elements to a queue that has {_queue.Count} elements."));
                                else
                                {
                                    foreach (var i in items)
                                        _queue.Enqueue(i);

                                    if (_queue.Count == 0)
                                    {
                                        if (IsClosed(retry.In1))
                                            CompleteStage();
                                        else
                                            Pull(retry.In1);
                                    }
                                    else
                                    {
                                        Pull(retry.In2);
                                        if (IsAvailable(retry.Out2))
                                        {
                                            Push(retry.Out2, _queue.Dequeue());
                                            _elementInCycle = true;
                                        }
                                    }
                                }
                            }

                        }
                    });

                    SetHandler(retry.Out2, onPull: () =>
                    {
                        if (!_elementInCycle && IsAvailable(retry.Out1))
                        {
                            if (_queue.Count == 0)
                                Pull(_retry.In1);
                            else
                            {
                                Push(retry.Out2, _queue.Dequeue());
                                _elementInCycle = true;
                                if (!HasBeenPulled(_retry.In2))
                                    Pull(_retry.In2);
                            }
                        }
                    }, onDownstreamFinish: () =>
                    {
                        //Do Nothing, intercept completion as downstream
                    });
                }

                private void PushAndCompleteIfLast(Tuple<Result<TO>, TS> item)
                {
                    Push(_retry.Out1, item);
                    if (IsClosed(_retry.In1) && _queue.Count == 0)
                        CompleteStage();
                }
            }

            #endregion

            private readonly long _limit;
            private readonly Func<TS, IEnumerable<Tuple<TI, TS>>> _retryWith;

            public RetryConcatCoordinator(long limit, Func<TS, IEnumerable<Tuple<TI, TS>>> retryWith)
            {
                _limit = limit;
                _retryWith = retryWith;

                In1 = new Inlet<Tuple<TI, TS>>("RetryConcat.ext.in");
                Out1 = new Outlet<Tuple<Result<TO>, TS>>("RetryConcat.ext.out");
                In2 = new Inlet<Tuple<Result<TO>, TS>>("RetryConcat.int.in");
                Out2 = new Outlet<Tuple<TI, TS>>("RetryConcat.int.out");
                Shape = new BidiShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>, Tuple<Result<TO>, TS>, Tuple<TI, TS>>(In1, Out1, In2, Out2);
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override BidiShape<Tuple<TI, TS>, Tuple<Result<TO>, TS>, Tuple<Result<TO>, TS>, Tuple<TI, TS>> Shape { get; }

            public Inlet<Tuple<TI, TS>> In1 { get; }
            public Outlet<Tuple<Result<TO>, TS>> Out1 { get; }
            public Inlet<Tuple<Result<TO>, TS>> In2 { get; }
            public Outlet<Tuple<TI, TS>> Out2 { get; }
        }
    }
}
