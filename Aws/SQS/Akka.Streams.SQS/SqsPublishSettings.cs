#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsPublishSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;

namespace Akka.Streams.SQS
{
    public sealed class SqsPublishSettings
    {
        public int MaxInFlight { get; }
        public static SqsPublishSettings Default = new SqsPublishSettings(maxInFlight: 10);
        
        public SqsPublishSettings(int maxInFlight)
        {
            if (maxInFlight <= 0) throw new ArgumentException($"{nameof(maxInFlight)} must be > 0", nameof(maxInFlight));
            MaxInFlight = maxInFlight;
        }
        
        /// <summary>
        /// Default: 10.
        /// </summary>
        public SqsPublishSettings WithMaxInFlight(int maxInFlight) => new SqsPublishSettings(maxInFlight: maxInFlight);
    }

    public sealed class SqsPublishGroupedSettings
    {
        public static SqsPublishGroupedSettings Default { get; } = new SqsPublishGroupedSettings(
            maxBatchSize: 10,
            maxBatchWait: TimeSpan.FromMilliseconds(500),
            concurrentRequests: 1);
        
        public int MaxBatchSize { get; }
        public TimeSpan MaxBatchWait { get; }
        public int ConcurrentRequests { get; }

        public SqsPublishGroupedSettings(
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
        
        public SqsPublishGroupedSettings Copy(
            int? maxBatchSize = null,
            TimeSpan? maxBatchWait = null,
            int? concurrentRequests = null) => new SqsPublishGroupedSettings(
            maxBatchSize: maxBatchSize ?? this.MaxBatchSize,
            maxBatchWait: maxBatchWait ?? this.MaxBatchWait,
            concurrentRequests: concurrentRequests ?? this.ConcurrentRequests);

        /// <summary>
        /// Default: 10.
        /// </summary>
        public SqsPublishGroupedSettings WithMaxBatchSize(int maxBatchSize) => Copy(maxBatchSize: maxBatchSize);
        
        /// <summary>
        /// Default: 500ms.
        /// </summary>
        public SqsPublishGroupedSettings WithMaxBatchWait(TimeSpan maxBatchWait) => Copy(maxBatchWait: maxBatchWait);
        
        /// <summary>
        /// Default: 1.
        /// </summary>
        public SqsPublishGroupedSettings WithConcurrentRequests(int concurrentRequests) => Copy(concurrentRequests: concurrentRequests);
    }

    public sealed class SqsPublishBatchSettings
    {
        public static SqsPublishBatchSettings Default { get; } = new SqsPublishBatchSettings(
            concurrentRequests: 1);
        
        public int ConcurrentRequests { get; }

        public SqsPublishBatchSettings(int concurrentRequests)
        {
            ConcurrentRequests = concurrentRequests;
        }
        
        /// <summary>
        /// Default: 1.
        /// </summary>
        public SqsPublishBatchSettings WithConcurrentRequests(int concurrentRequests) => 
            new SqsPublishBatchSettings(concurrentRequests: concurrentRequests);
    }
}