using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using System;

namespace Akka.Streams.Azure.StorageQueue
{
    /// <summary>
    /// Wrapper for the <see cref="CloudQueue.AddMessageAsync(CloudQueueMessage)"/> parameter
    /// </summary>
    public class AddRequestOptions : RequestOptions
    {
        /// <summary>
        /// Creates a new instance of the <see cref="AddRequestOptions"/>
        /// </summary>
        /// <param name="timeToLive">A <see cref="TimeSpan"/> specifying the maximum time to allow the message to be in the ueue, or null.</param>
        /// <param name="initialVisibilityDelay">A <see cref="TimeSpan"/> specifying the interval of time from now during which the message will be invisible. If null then the message will be visible immediately.</param>
        /// <param name="queueRequestOptions">A <see cref="QueueRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        public AddRequestOptions(TimeSpan? timeToLive = null, TimeSpan? initialVisibilityDelay = null,
            QueueRequestOptions queueRequestOptions = null, OperationContext operationContext = null)
            : base(queueRequestOptions, operationContext)
        {
            TimeToLive = timeToLive;
            InitialVisibilityDelay = initialVisibilityDelay;
        }

        public TimeSpan? TimeToLive { get; }

        public TimeSpan? InitialVisibilityDelay { get; }
    }

    /// <summary>
    /// Wraper for the <see cref="CloudQueue.GetMessagesAsync(int)"/> parameter
    /// </summary>
    public class GetRequestOptions : RequestOptions
    {
        /// <summary>
        /// Creates a new instance of the <see cref="GetRequestOptions"/>
        /// </summary>
        /// <param name="visibilityTimeout">A <see cref="TimeSpan"/> specifying the visibility timeout interval.</param>
        /// <param name="queueRequestOptions">A <see cref="QueueRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        public GetRequestOptions(TimeSpan? visibilityTimeout = null, QueueRequestOptions queueRequestOptions = null,
            OperationContext operationContext = null) : base(queueRequestOptions, operationContext)
        {
            VisibilityTimeout = visibilityTimeout;
        }

        public TimeSpan? VisibilityTimeout { get; }
    }

    /// <summary>
    /// Wrapper for the <see cref="CloudQueue"/> request parameter
    /// </summary>
    public class RequestOptions
    {
        /// <summary>
        /// Creates a new instance of the <see cref="RequestOptions"/> 
        /// </summary>
        /// <param name="queueRequestOptions">A <see cref="QueueRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        public RequestOptions(QueueRequestOptions queueRequestOptions = null, OperationContext operationContext = null)
        {
            QueueRequestOptions = queueRequestOptions;
            OperationContext = operationContext;
        }

        public QueueRequestOptions QueueRequestOptions { get; }

        public OperationContext OperationContext { get; }
    }
}