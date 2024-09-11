//-----------------------------------------------------------------------
// <copyright file="EventHubSink.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Decider = Akka.Streams.Supervision.Decider;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Azure.EventHub
{
    /// <summary>
    /// A <see cref="Sink{TIn,TMat}"/> for the Azure EventHub
    /// </summary>
    public class EventHubSink : GraphStageWithMaterializedValue<SinkShape<IEnumerable<EventData>>, Task>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly EventHubSink _sink;
            private readonly TaskCompletionSource<NotUsed> _completion;
            private readonly Decider _decider;
            private bool _isSendInProgress;
            private IActorRef? _self;
            private readonly List<EventData> _pendingSend = new List<EventData>();

            public Logic(EventHubSink sink, Attributes inheritedAttributes, TaskCompletionSource<NotUsed> completion) : base(sink.Shape)
            {
                _sink = sink;
                _completion = completion;
                _decider = inheritedAttributes.GetDeciderOrDefault();
                
                SetHandler(sink.In,
                    onPush: () => TrySend(Grab(sink.In).ToArray()),
                    onUpstreamFinish: () =>
                    {
                        // It is most likely that we receive the finish event before the task from the last element has finished
                        // so if the task is still running we need to complete the stage later
                        if (!_isSendInProgress)
                            Finish();
                    },
                    onUpstreamFailure: ex =>
                    {
                        _completion.TrySetException(ex);
                        // We have set KeepGoing to true so we need to fail the stage manually
                        FailStage(ex);
                    });
            }

            public override void PreStart()
            {
                _self = GetStageActor(args =>
                {
                    var (_, msg) = args;
                    switch (msg)
                    {
                        case Status.Success _:
                            if (_pendingSend.Count > 0)
                            {
                                var events = _pendingSend.ToArray();
                                _pendingSend.Clear();
                                TrySend(events);
                            }
                            else
                            {
                                _isSendInProgress = false;
                                PullOrComplete();
                            }
                            break;
                        case Status.Failure fail:
                            _isSendInProgress = false;
                            var cause = fail.Cause;
                            switch (_decider(cause))
                            {
                                case Directive.Stop:
                                    // Throw
                                    _completion.TrySetException(cause);
                                    FailStage(cause);
                                    break;
                                case Directive.Resume:
                                    // Take the next element or complete
                                    PullOrComplete();
                                    break;
                                case Directive.Restart:
                                    // Try again
                                    TrySend((EventData[])fail.State);
                                    break;
                                case var unknown:
                                    throw new ArgumentException(
                                        $"Invalid failure directive {unknown}. Valid values are " +
                                        $"{nameof(Directive.Stop)}, {nameof(Directive.Restart)}, " +
                                        $"and {nameof(Directive.Resume)}");
                            }
                            break;
                        default:
                            Log.Warning($"Unhandled EventHubSink message: [{msg.GetType()}]");
                            break;
                    }
                }).Ref;

                // Keep going even if the upstream has finished so that we can process the task from the last element
                SetKeepGoing(true);
                // Request the first element
                Pull(_sink.In);
            }

            private void TrySend(EventData[] events)
            {
                _isSendInProgress = true;
                var localEvents = events;
                SendBatch(events: localEvents)
                    .PipeTo(
                        recipient: _self,
                        sender: _self, 
                        failure: ex => new Status.Failure(ex, localEvents));
            }

            private async Task<Status> SendBatch(IEnumerable<EventData> events)
            {
                var batch = await _sink._client.CreateBatchAsync();
                foreach (var eventData in events)
                {
                    if(!batch.TryAdd(eventData))
                    {
                        _pendingSend.Add(eventData);
                    }
                }

                await _sink._client.SendAsync(batch);
                return Status.Success.Instance;
            }

            private void PullOrComplete()
            {
                if (IsClosed(_sink.In))
                    Finish();
                else
                    Pull(_sink.In);
            }

            private void Finish()
            {
                _completion.TrySetResult(NotUsed.Instance);
                CompleteStage();
            }
        }

        #endregion

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> for the Azure EventHub
        /// </summary>
        /// <param name="producer">The <see cref="EventHubProducerClient"/> that sends the events to the EventHub</param>
        /// <returns>The <see cref="Sink{TIn,TMat}"/> for the Azure EventHub</returns>
        public static Sink<IEnumerable<EventData>, Task> Create(EventHubProducerClient producer)
        {
            return Sink.FromGraph(new EventHubSink(producer));
        }

        private readonly EventHubProducerClient _client;

        /// <summary>
        /// Create a new instance of the <see cref="EventHubSink"/>
        /// </summary>
        /// <param name="client">The <see cref="EventHubProducerClient"/> that sends the events to the EventHub</param>
        public EventHubSink(EventHubProducerClient client)
        {
            _client = client;
            Shape = new SinkShape<IEnumerable<EventData>>(In);
        }

        public Inlet<IEnumerable<EventData>> In { get; } = new Inlet<IEnumerable<EventData>>("EventHubSink.In");

        public override SinkShape<IEnumerable<EventData>> Shape { get; }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("EventHubSink");

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<NotUsed>();
            var logic = new Logic(this, inheritedAttributes, completion);
            return new LogicAndMaterializedValue<Task>(logic, completion.Task);
        }
    }
}
