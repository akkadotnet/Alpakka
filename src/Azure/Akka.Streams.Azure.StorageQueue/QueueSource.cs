using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Azure.Utils;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Akka.Streams.Azure.StorageQueue
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> for the Azure Storage Queue
    /// </summary>
    public class QueueSource : GraphStage<SourceShape<QueueMessage>>
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string TimerKey = "PollTimer";
            private readonly QueueSource _source;
            private Action<Task<Response<QueueMessage[]>>> _messagesReceived;
            private readonly Decider _decider;

            public Logic(QueueSource source, Attributes attributes) : base(source.Shape)
            {
                _source = source;
                _decider = attributes.GetDeciderOrDefault();

                SetHandler(source.Out, PullQueue);
            }

            private void PullQueue() =>
                _source._queue.ReceiveMessagesAsync(
                        _source._prefetchCount, 
                        _source._options.VisibilityTimeout)
                    .ContinueWith(_messagesReceived);

            protected override void OnTimer(object timerKey) => PullQueue();

            public override void PreStart()
                => _messagesReceived = GetAsyncCallback<Task<Response<QueueMessage[]>>>(OnMessagesReceived);

            private void OnMessagesReceived(Task<Response<QueueMessage[]>> task)
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    if (_decider(task.Exception) == Directive.Stop)
                        FailStage(task.Exception);
                    else
                        ScheduleOnce(TimerKey, _source._pollInterval);

                    return;
                }

                // Try again if the queue is empty
                if (task.Result == null || (task.Result.Value != null && !task.Result.Value.Any()))
                    ScheduleOnce(TimerKey, _source._pollInterval);
                else
                    EmitMultiple(_source.Out, task.Result.Value);
            }
        }

        #endregion

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure Storage Queue
        /// </summary>
        /// <param name="queue">The queue</param>
        /// <param name="prefetchCount">The number of messages that should be read from the queue at once</param>
        /// <param name="pollInterval">The interval in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        /// <param name="options">The options for the <see cref="QueueClient.ReceiveMessagesAsync()"/> call</param>
        /// <returns>The <see cref="Source{TOut,TMat}"/> for the Azure Storage Queue</returns>
        public static Source<QueueMessage, NotUsed> Create(QueueClient queue, int prefetchCount = 10, TimeSpan? pollInterval = null, GetRequestOptions options = null)
        {
            return Source.FromGraph(new QueueSource(queue, prefetchCount, pollInterval, options));
        }

        private readonly QueueClient _queue;
        private readonly int _prefetchCount;
        private readonly GetRequestOptions _options;
        private readonly TimeSpan _pollInterval;

        /// <summary>
        /// Create a new instance of the <see cref="QueueSource"/>
        /// </summary>
        /// <param name="queue">The queue</param>
        /// <param name="prefetchCount">The number of messages that should be read from the queue at once</param>
        /// <param name="pollInterval">The interval in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        /// <param name="options">The options for the <see cref="QueueClient.ReceiveMessagesAsync()"/> call</param>
        public QueueSource(QueueClient queue, int prefetchCount = 10, TimeSpan? pollInterval = null, GetRequestOptions options = null)
        {
            _queue = queue;
            _prefetchCount = prefetchCount;
            _options = options ?? new GetRequestOptions();
            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(10);

            Shape = new SourceShape<QueueMessage>(Out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("QueueSource");

        public Outlet<QueueMessage> Out { get; } = new Outlet<QueueMessage>("QueueSource.Out");

        public override SourceShape<QueueMessage> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
    }
}
