using System;
using Azure.Storage;
using Azure.Storage.Queues;

namespace Akka.Streams.Azure.StorageQueue
{
    /// <summary>
    /// Wrapper for the <see cref="QueueClient.SendMessageAsync(string)"/> parameter
    /// </summary>
    public class AddRequestOptions
    {
        /// <summary>
        /// Creates a new instance of the <see cref="AddRequestOptions"/>
        /// </summary>
        /// <param name="timeToLive">A <see cref="TimeSpan"/> specifying the maximum time to allow the message to be in the ueue, or null.</param>
        /// <param name="initialVisibilityDelay">A <see cref="TimeSpan"/> specifying the interval of time from now during which the message will be invisible. If null then the message will be visible immediately.</param>
        public AddRequestOptions(TimeSpan? timeToLive = null, TimeSpan? initialVisibilityDelay = null)
        {
            TimeToLive = timeToLive;
            InitialVisibilityDelay = initialVisibilityDelay;
        }

        public TimeSpan? TimeToLive { get; }

        public TimeSpan? InitialVisibilityDelay { get; }
    }

    /// <summary>
    /// Wrapper for the <see cref="QueueClient.ReceiveMessagesAsync()"/> parameter
    /// </summary>
    public class GetRequestOptions
    {
        /// <summary>
        /// Creates a new instance of the <see cref="GetRequestOptions"/>
        /// </summary>
        /// <param name="visibilityTimeout">A <see cref="TimeSpan"/> specifying the visibility timeout interval.</param>
        public GetRequestOptions(TimeSpan? visibilityTimeout = null)
        {
            VisibilityTimeout = visibilityTimeout;
        }

        public TimeSpan? VisibilityTimeout { get; }
    }
}