using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Azure.Utils;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Azure.Messaging.ServiceBus;

namespace Akka.Streams.Azure.ServiceBus
{
    public static class ServiceBusSource
    {
        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure ServiceBus  
        /// </summary>
        /// <param name="client">The client</param>
        /// <param name="maxMessageCount">The maximum number of messages to receive in a batch</param>
        /// <param name="serverWaitTime">The time span that the server will wait for the message batch to arrive before it times out. Default = 3 seconds</param>
        /// <param name="pollInterval">The interval in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        public static Source<ServiceBusReceivedMessage, NotUsed> Create(ServiceBusReceiver client, int maxMessageCount = 100, TimeSpan? serverWaitTime = null, TimeSpan? pollInterval = null)
        {
            return Create(client, msg => msg, maxMessageCount, serverWaitTime, pollInterval);
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure ServiceBus  
        /// </summary>
        /// <param name="client">The client</param>
        /// <param name="extractor">Extracts the message from the <see cref="ServiceBusReceivedMessage"/></param>
        /// <param name="maxMessageCount">The maximum number of messages to receive in a batch</param>
        /// <param name="serverWaitTime">The time span that the server will wait for the message batch to arrive before it times out. Default = 3 seconds</param>
        /// <param name="pollInterval">The interval in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        public static Source<T, NotUsed> Create<T>(ServiceBusReceiver client, Func<ServiceBusReceivedMessage, T> extractor, int maxMessageCount = 100, TimeSpan? serverWaitTime = null, TimeSpan? pollInterval = null)
        {
            return Source.FromGraph(new ServiceBusSource<T>(client, maxMessageCount, serverWaitTime, pollInterval, extractor));
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> for the Azure ServiceBus.
        /// <para>
        /// Note: The message of type <typeparam name="T"/> is extracted via <see cref="BinaryData.ToObjectFromJson{T}"/> and the <see cref="ServiceBusReceivedMessage"/> is automatically completed.
        /// </para>
        /// </summary>
        /// <param name="client">The client</param>
        /// <param name="maxMessageCount">The maximum number of messages to receive in a batch</param>
        /// <param name="serverWaitTime">The time span that the server will wait for the message batch to arrive before it times out. Default = 3 seconds</param>
        /// <param name="pollInterval">The interval in witch the queue should be polled if it is empty. Default = 10 seconds</param>
        public static Source<T, NotUsed> Create<T>(ServiceBusReceiver client, int maxMessageCount = 100, TimeSpan? serverWaitTime = null, TimeSpan? pollInterval = null)
        {
            return Create(client, msg =>
            {
                var result = msg.Body.ToObjectFromJson<T>();
                client.CompleteMessageAsync(msg);
                return result;
            }, maxMessageCount, serverWaitTime, pollInterval);
        }
    }


    /// <summary>
    /// a <see cref="Source{TOut,TMat}"/> for the Azure ServiceBus
    /// </summary>
    internal class ServiceBusSource<T> : GraphStage<SourceShape<T>>
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string TimerKey = "PollTimer";
            private readonly ServiceBusSource<T> _source;
            private readonly Decider _decider;
            private Action<Task<IReadOnlyList<ServiceBusReceivedMessage>>> _messagesReceived;

            public Logic(ServiceBusSource<T> source, Attributes attributes) :  base(source.Shape)
            {
                _source = source;
                _decider = attributes.GetDeciderOrDefault();

                SetHandler(source.Out, PullQueue);
            }

            public override void PreStart()
                => _messagesReceived = GetAsyncCallback<Task<IReadOnlyList<ServiceBusReceivedMessage>>>(OnMessagesReceived);
            
            private void PullQueue() =>
                _source._client.ReceiveMessagesAsync(_source._maxMessageCount, _source._serverWaitTime)
                    .ContinueWith(_messagesReceived);

            private void OnMessagesReceived(Task<IReadOnlyList<ServiceBusReceivedMessage>> task)
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
                {
                    EmitMultiple(_source.Out, task.Result.Select(_source._extractor));
                }
            }

            protected override void OnTimer(object timerKey) => PullQueue();
        }

        #endregion

        private readonly ServiceBusReceiver _client;
        private readonly int _maxMessageCount;
        private readonly TimeSpan _serverWaitTime;
        private readonly TimeSpan _pollInterval;
        private readonly Func<ServiceBusReceivedMessage, T> _extractor;

        internal ServiceBusSource(
            ServiceBusReceiver client, 
            int maxMessageCount, 
            TimeSpan? serverWaitTime, 
            TimeSpan? pollInterval, 
            Func<ServiceBusReceivedMessage, T> extractor)
        {
            _client = client;
            _maxMessageCount = maxMessageCount;
            _serverWaitTime = serverWaitTime ?? TimeSpan.FromSeconds(3);
            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(10);
            _extractor = extractor;

            Shape = new SourceShape<T>(Out);
        }

        public Outlet<T> Out { get; } = new Outlet<T>("ServiceBusSource.Out");

        public override SourceShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);
    }
}
