#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsAckSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;

namespace Akka.Streams.SQS
{
    public class SqsAckSettings
    {
        public static SqsAckSettings Default { get; } = new SqsAckSettings(maxInFlight: 10);
        
        public int MaxInFlight { get; }

        public SqsAckSettings(int maxInFlight)
        {
            if (maxInFlight <= 0)
                throw new ArgumentException($"{nameof(maxInFlight)} must be > 0", nameof(maxInFlight));
            
            MaxInFlight = maxInFlight;
        }
        
        /// <summary>
        /// Default: 10.
        /// </summary>
        public SqsAckSettings WithMaxInFlight(int maxInFlight) => new SqsAckSettings(maxInFlight: maxInFlight);
    }
    public sealed class SqsAckGroupedSettings
    {
        public static SqsAckGroupedSettings Default { get; } = new SqsAckGroupedSettings(
            maxBatchSize: 10,
            maxBatchWait: TimeSpan.FromMilliseconds(500),
            concurrentRequests: 1);
        
        public int MaxBatchSize { get; }
        public TimeSpan MaxBatchWait { get; }
        public int ConcurrentRequests { get; }

        public SqsAckGroupedSettings(
            int maxBatchSize,
            TimeSpan maxBatchWait,
            int concurrentRequests)
        {
            if (maxBatchSize <= 0 || maxBatchSize > 10)
                throw new ArgumentException($"Invalid value for {nameof(maxBatchSize)}: {maxBatchSize}. It should be 0 < maxBatchSize < 10, due to the Amazon SQS requirements.", nameof(maxBatchSize));
            
            MaxBatchSize = maxBatchSize;
            MaxBatchWait = maxBatchWait;
            ConcurrentRequests = concurrentRequests;
        }
        
        public SqsAckGroupedSettings Copy(
            int? maxBatchSize = null,
            TimeSpan? maxBatchWait = null,
            int? concurrentRequests = null) => new SqsAckGroupedSettings(
            maxBatchSize: maxBatchSize ?? this.MaxBatchSize,
            maxBatchWait: maxBatchWait ?? this.MaxBatchWait,
            concurrentRequests: concurrentRequests ?? this.ConcurrentRequests);

        /// <summary>
        /// Default: 10.
        /// </summary>
        public SqsAckGroupedSettings WithMaxBatchSize(int maxBatchSize) => Copy(maxBatchSize: maxBatchSize);
        
        /// <summary>
        /// Default: 500ms.
        /// </summary>
        public SqsAckGroupedSettings WithMaxBatchWait(TimeSpan maxBatchWait) => Copy(maxBatchWait: maxBatchWait);
        
        /// <summary>
        /// Default: 1.
        /// </summary>
        public SqsAckGroupedSettings WithConcurrentRequests(int concurrentRequests) => Copy(concurrentRequests: concurrentRequests);
    }

    public sealed class SqsAckBatchSettings
    {
        public static SqsAckBatchSettings Default { get; } = new SqsAckBatchSettings(
            concurrentRequests: 1);
        
        public int ConcurrentRequests { get; }

        public SqsAckBatchSettings(int concurrentRequests)
        {
            ConcurrentRequests = concurrentRequests;
        }
        
        /// <summary>
        /// Default: 1.
        /// </summary>
        public SqsAckBatchSettings WithConcurrentRequests(int concurrentRequests) => 
            new SqsAckBatchSettings(concurrentRequests: concurrentRequests);
    }
}