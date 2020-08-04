using System;
using System.Threading.Tasks;
using Akka.Streams.Azure.Utils;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Akka.Streams.Azure.StorageQueue
{
    /// <summary>
    /// A <see cref="Sink{TIn,TMat}"/> for the Azure Storage Queue
    /// </summary>
    public class QueueSink : GraphStageWithMaterializedValue<SinkShape<CloudQueueMessage>, Task>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly QueueSink _sink;
            private readonly TaskCompletionSource<NotUsed> _completion;
            private Action<Tuple<Task, CloudQueueMessage>> _messageAddedCallback;
            private bool _isAddInProgress;
            private readonly Decider _decider;

            public Logic(QueueSink sink, Attributes attributes, TaskCompletionSource<NotUsed> completion) : base(sink.Shape)
            {
                _sink = sink;
                _completion = completion;
                _decider = attributes.GetDeciderOrDefault();

                SetHandler(sink.In,
                    onPush: () => TryAdd(Grab(_sink.In)),
                    onUpstreamFinish: () =>
                    {
                        // It is most likely that we receive the finish event before the task from the last element has finished
                        // so if the task is still running we need to complete the stage later
                        if (!_isAddInProgress)
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
                _messageAddedCallback = GetAsyncCallback<Tuple<Task, CloudQueueMessage>>(OnMessageAdded);
                // Request the first element
                Pull(_sink.In);
            }

            private void TryAdd(CloudQueueMessage message)
            {
                _isAddInProgress = true;
                _sink._queue.AddMessageAsync(message, _sink._options.TimeToLive, _sink._options.InitialVisibilityDelay,
                    _sink._options.QueueRequestOptions, _sink._options.OperationContext)
                    .ContinueWith(t => _messageAddedCallback(Tuple.Create(t, message)));
            }
            
            private void OnMessageAdded(Tuple<Task, CloudQueueMessage> t)
            {
                _isAddInProgress = false;
                var task = t.Item1;
                var message = t.Item2;

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
                            TryAdd(message);
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
        /// Creates a <see cref="Sink{TIn,TMat}"/> for the Azure Storage Queue
        /// </summary>
        /// <param name="queue">The queue</param>
        /// <param name="options">The options for the <see cref="CloudQueue.AddMessageAsync(CloudQueueMessage)"/> call</param>
        /// <returns>The <see cref="Sink{TIn,TMat}"/> for the Azure Storage Queue</returns>
        public static Sink<CloudQueueMessage, Task> Create(CloudQueue queue, AddRequestOptions options = null)
        {
            return Sink.FromGraph(new QueueSink(queue, options));
        }

        private readonly CloudQueue _queue;
        private readonly AddRequestOptions _options;

        /// <summary>
        /// Create a new instance of the <see cref="QueueSink"/>
        /// </summary>
        /// <param name="queue">The queue</param>
        /// <param name="options">The options for the <see cref="CloudQueue.AddMessageAsync(CloudQueueMessage)"/> call</param>
        public QueueSink(CloudQueue queue, AddRequestOptions options = null)
        {
            _queue = queue;
            _options = options ?? new AddRequestOptions();
            Shape = new SinkShape<CloudQueueMessage>(In);
        }

        public Inlet<CloudQueueMessage> In { get; } = new Inlet<CloudQueueMessage>("QueueSink.In");

        public override SinkShape<CloudQueueMessage> Shape { get; }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("QueueSink");

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<NotUsed>();
            return new LogicAndMaterializedValue<Task>(new Logic(this, inheritedAttributes, completion), completion.Task);
        }
    }
}
