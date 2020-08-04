#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsSourceSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;

namespace Akka.Streams.SQS
{

    public sealed class SqsSourceSettings
    {
        public static SqsSourceSettings Default { get; } = new SqsSourceSettings(
            waitTime: TimeSpan.FromSeconds(20), 
            maxBufferSize: 100,
            parallelism: 1,
            maxBatchSize: 10,
            attributeNames: Array.Empty<AttributeName>(),
            messageAttributeNames: Array.Empty<MessageAttributeName>(),
            closeOnEmptyReceive: false,
            visibilityTimeout: null
        );
        
        public TimeSpan WaitTime { get; }
        public int MaxBufferSize { get; }
        public int Parallelism { get; }
        public int MaxBatchSize { get; }
        public IReadOnlyList<AttributeName> AttributeNames { get; }
        public IReadOnlyList<MessageAttributeName> MessageAttributeNames { get; }
        public bool CloseOnEmptyReceive { get; }
        public TimeSpan? VisibilityTimeout { get; }

        public SqsSourceSettings(
            TimeSpan waitTime,
            int maxBufferSize,
            int parallelism,
            int maxBatchSize,
            IReadOnlyList<AttributeName> attributeNames,
            IReadOnlyList<MessageAttributeName> messageAttributeNames,
            bool closeOnEmptyReceive,
            TimeSpan? visibilityTimeout)
        {
            if (maxBatchSize > maxBufferSize)
                throw new ArgumentException($"{nameof(maxBatchSize)} must be lower or equal than {nameof(maxBufferSize)}", nameof(maxBatchSize));
            // SQS requirements
            if (maxBatchSize < 1 || maxBatchSize > 10)
                throw new ArgumentException($"{nameof(maxBatchSize)} must be between 1 and 10", nameof(maxBatchSize));
            if (waitTime.TotalSeconds > 20)
                throw new ArgumentException($"{nameof(waitTime)} must be between 0 and 20 seconds", nameof(waitTime));
            
            WaitTime = waitTime;
            MaxBufferSize = maxBufferSize;
            Parallelism = parallelism;
            MaxBatchSize = maxBatchSize;
            AttributeNames = attributeNames;
            MessageAttributeNames = messageAttributeNames;
            CloseOnEmptyReceive = closeOnEmptyReceive;
            VisibilityTimeout = visibilityTimeout;
        }
        
        public SqsSourceSettings Copy(
            TimeSpan? waitTime = null,
            int? maxBufferSize = null,
            int? parallelism = null,
            int? maxBatchSize = null,
            IReadOnlyList<AttributeName> attributeNames = null,
            IReadOnlyList<MessageAttributeName> messageAttributeNames = null,
            bool? closeOnEmptyReceive = null,
            TimeSpan? visibilityTimeout = null) => 
            new SqsSourceSettings(
                waitTime: waitTime ?? this.WaitTime,
                maxBufferSize: maxBufferSize ?? this.MaxBufferSize,
                parallelism: parallelism ?? this.Parallelism,
                maxBatchSize: maxBatchSize ?? this.MaxBatchSize,
                attributeNames: attributeNames ?? this.AttributeNames,
                messageAttributeNames: messageAttributeNames ?? this.MessageAttributeNames,
                closeOnEmptyReceive: closeOnEmptyReceive ?? this.CloseOnEmptyReceive,
                visibilityTimeout: visibilityTimeout ?? this.VisibilityTimeout);

        /// <summary>
        /// The duration in seconds for which the call waits for a message to arrive in the queue before returning.
        /// (see WaitTimeSeconds in AWS docs).
        /// Default: 20 seconds
        /// </summary>
        public SqsSourceSettings WithWaitTime(TimeSpan waitTime) => Copy(waitTime: waitTime);
        
        /// <summary>
        /// Internal buffer size used by the Source.
        /// 
        /// Default: 100 messages
        /// </summary>
        public SqsSourceSettings WithMaxBufferSize(int maxBufferSize) => Copy(maxBufferSize: maxBufferSize);
        
        /// <summary>
        /// The maximum number of messages to return (see MaxNumberOfMessages in AWS docs).
        /// 
        /// Default: 10 messages
        /// </summary>
        public SqsSourceSettings WithMaxBatchSize(int maxBatchSize) => Copy(maxBatchSize: maxBatchSize);

        public SqsSourceSettings WithAttributes(params AttributeName[] attributes) => Copy(attributeNames: attributes);
        
        public SqsSourceSettings WithMessageAttributes(params MessageAttributeName[] attributes) => 
            Copy(messageAttributeNames: attributes);

        /// <summary>
        /// If true, the source completes when no messages are available.
        /// Default: false.
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        public SqsSourceSettings WithCloseOnEmptyReceive(bool enable) => Copy(closeOnEmptyReceive: enable);

        /// <summary>
        /// The period of time (in seconds) during which Amazon SQS prevents other consumers
        /// from receiving and processing an already received message (see Amazon SQS doc)
        /// 
        /// Default: None - taken from the SQS queue configuration
        /// </summary>
        public SqsSourceSettings WithVisibilityTimeout(TimeSpan timeout) => Copy(visibilityTimeout: timeout);
    }
}