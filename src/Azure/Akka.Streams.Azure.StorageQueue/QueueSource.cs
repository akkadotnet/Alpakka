using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Azure.Utils;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Akka.Streams.Azure.StorageQueue
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> for the Azure Storage Queue
    /// </summary>
    public class QueueSource : GraphStage<SourceShape<CloudQueueMessage>>
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string TimerKey = "PollTimer";
            private readonly QueueSource _source;
            private Action<Task<IEnumerable<CloudQueueMessage>>> _messagesReceived;
            private readonly Decider _decider;

            public Logic(QueueSource source, Attributes attributes) : base(source.Shape)
            {
                _source = source;
                _decider = attributes.GetDeciderOrDefault();

                SetHandler(source.Out, PullQueue);
            }

            private void PullQueue() =>
                _source._queue.GetMessagesAsync(_source._prefetchCount, _source._options.VisibilityTimeout,
                    _source._options.QueueRequestOptions, _source._options.OperationContext)
                    .ContinueWith(_messagesReceived);

            protected override void OnTimer(object timerKey) => PullQueue();

            public override void PreStart()
                => _messagesReceived = GetAsyncCallback<Task<IEnumerable<CloudQueueMessage>>>(OnMessagesReceived);

            private void OnMessagesReceived(Task<IEnumerable<CloudQueueMessage>> task)
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
                if (task.Result == null || !task.Result.Any())
                    ScheduleOnce(TimerKey, _source._pollInterval);
                else
                    EmitMultiple(_source.Out, task.Result);
            }
        }

        #endregion

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure Storage Queue
        /// </summary>
        /// <param name="queue">The queue</param>
        /// <param name="prefetchCount">The number of messages that should be read from the queue at once</param>
        /// <param name="pollInterval">The intervall in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        /// <param name="options">The options for the <see cref="CloudQueue.GetMessagesAsync(int)"/> call</param>
        /// <returns>The <see cref="Source{TOut,TMat}"/> for the Azure Storage Queue</returns>
        public static Source<CloudQueueMessage, NotUsed> Create(CloudQueue queue, int prefetchCount = 10, TimeSpan? pollInterval = null, GetRequestOptions options = null)
        {
            return Source.FromGraph(new QueueSource(queue, prefetchCount, pollInterval, options));
        }

        private readonly CloudQueue _queue;
        private readonly int _prefetchCount;
        private readonly GetRequestOptions _options;
        private readonly TimeSpan _pollInterval;

        /// <summary>
        /// Create a new instance of the <see cref="QueueSource"/>
        /// </summary>
        /// <param name="queue">The queue</param>
        /// <param name="prefetchCount">The number of messages that should be read from the queue at once</param>
        /// <param name="pollInterval">The intervall in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        /// <param name="options">The options for the <see cref="CloudQueue.GetMessagesAsync(int)"/> call</param>
        public QueueSource(CloudQueue queue, int prefetchCount = 10, TimeSpan? pollInterval = null, GetRequestOptions options = null)
        {
            _queue = queue;
            _prefetchCount = prefetchCount;
            _options = options ?? new GetRequestOptions();
            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(10);

            Shape = new SourceShape<CloudQueueMessage>(Out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("QueueSource");

        public Outlet<CloudQueueMessage> Out { get; } = new Outlet<CloudQueueMessage>("QueueSource.Out");

        public override SourceShape<CloudQueueMessage> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
    }
}
