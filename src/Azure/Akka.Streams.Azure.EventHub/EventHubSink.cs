using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Azure.Utils;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Microsoft.Azure.EventHubs;

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
            private Action<Tuple<Task, List<EventData>>> _eventsSend;
            private bool _isSendInProgress;

            public Logic(EventHubSink sink, Attributes inheritedAttributes, TaskCompletionSource<NotUsed> completion) : base(sink.Shape)
            {
                _sink = sink;
                _completion = completion;
                _decider = inheritedAttributes.GetDeciderOrDefault();

                SetHandler(sink.In,
                    onPush: () => TrySend(Grab(sink.In).ToList()),
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
                // Keep going even if the upstream has finished so that we can process the task from the last element
                SetKeepGoing(true);
                _eventsSend = GetAsyncCallback<Tuple<Task, List<EventData>>>(OnEventsSend);
                // Request the first element
                Pull(_sink.In);
            }

            private void TrySend(List<EventData> events)
            {
                _isSendInProgress = true;
                _sink._client.SendAsync(events).ContinueWith(t => _eventsSend(Tuple.Create(t, events)));
            }

            private void OnEventsSend(Tuple<Task, List<EventData>> t)
            {
                _isSendInProgress = false;
                var task = t.Item1;
                var events = t.Item2;

                if (task.IsFaulted || task.IsCanceled)
                {
                    switch (_decider(task.Exception))
                    {
                        case Directive.Stop:
                            // Throw
                            _completion.TrySetException(task.Exception);
                            FailStage(task.Exception);
                            break;
                        case Directive.Resume:
                            // Try again
                            TrySend(events);
                            break;
                        case Directive.Restart:
                            // Take the next element or complete
                            PullOrComplete();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                    PullOrComplete();

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
        /// <param name="client">The <see cref="EventHubClient"/> that sends the events to the EventHub</param>
        /// <returns>The <see cref="Sink{TIn,TMat}"/> for the Azure EventHub</returns>
        public static Sink<IEnumerable<EventData>, Task> Create(EventHubClient client)
        {
            return Sink.FromGraph(new EventHubSink(client));
        }

        private readonly EventHubClient _client;

        /// <summary>
        /// Create a new instance of the <see cref="EventHubSink"/>
        /// </summary>
        /// <param name="client">The <see cref="EventHubClient"/> that sends the events to the EventHub</param></param>
        public EventHubSink(EventHubClient client)
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
