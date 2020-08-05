#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisFlowSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;

namespace Akka.Streams.Kinesis
{
    /// <summary>
    /// Type of a retry backoff used by <see cref="KinesisFlow"/>
    /// when pushing the request to Amazon Kinesis streams.
    /// </summary>
    public enum RetryBackoffStrategy
    {
        /// <summary>
        /// A backoff delay that will be increasing <see cref="KinesisFlowSettings.RetryInitialTimeout"/>
        /// by factor of two, eg. 500ms, 1s, 2s, 4s, 8s etc.
        /// </summary>
        Exponential,

        /// <summary>
        /// A backoff strategy that will apply a constant <see cref="KinesisFlowSettings.RetryInitialTimeout"/>
        /// delay before the next attempt of pushing events to Amazon Kinesis.
        /// </summary>
        Linear
    }

    /// <summary>
    /// Immutable settings class to be used by <see cref="KinesisFlow"/> factory methods.
    /// </summary>
    public sealed class KinesisFlowSettings
    {
        private const int MAX_RECORDS_PER_REQUEST = 500;
        private const int MAX_RECORDS_PER_SHARD_PER_SECOND = 1000;
        private const int MAX_BYTES_PER_SHARD_PER_SECOND = 1000000;

        /// <summary>
        /// Default <see cref="KinesisFlowSettings"/>, assuming only 1 active shard.
        /// </summary>
        public static KinesisFlowSettings Default = ByNumberOfShard(1);

        private static KinesisFlowSettings ByNumberOfShard(int shards) => new KinesisFlowSettings(
            parallelism: shards * (MAX_RECORDS_PER_SHARD_PER_SECOND / MAX_RECORDS_PER_REQUEST),
            maxBatchSize: MAX_RECORDS_PER_REQUEST,
            backoffStrategy: RetryBackoffStrategy.Exponential,
            retryInitialTimeout: TimeSpan.FromMilliseconds(500),
            maxRecordsPerSecond: shards * MAX_RECORDS_PER_SHARD_PER_SECOND,
            maxBytesPerSecond: shards * MAX_BYTES_PER_SHARD_PER_SECOND,
            maxRetries: 5);

        /// <summary>
        /// Maximum number of concurrent put requests send to Amazon Kinesis.
        /// Default value: 2
        /// </summary>
        public int Parallelism { get; }

        /// <summary>
        /// Maximum size of a single batch of records send to Amazon Kinesis in a single packet.
        /// Default value: 500
        /// </summary>
        public int MaxBatchSize { get; }

        /// <summary>
        /// A backoff strategy used when trying to redeliver failed send requests.
        /// Default value: <see cref="RetryBackoffStrategy.Exponential"/>
        /// </summary>
        public RetryBackoffStrategy BackoffStrategy { get; }

        /// <summary>
        /// Maximum number of records send per second. This is used to avoid native Amazon Kinesis
        /// throttling mechanism, that will not allow to send more data that provided threshold.
        /// Default value: 1000
        /// </summary>
        public int MaxRecordsPerSecond { get; }

        /// <summary>
        /// Maximum total messages payload size send each second. This is used to avoid native Amazon Kinesis
        /// throttling mechanism, that will not allow to send more data that provided threshold.
        /// Default value: 1MB (1 000 000 bytes).
        /// </summary>
        public int MaxBytesPerSecond { get; }

        /// <summary>
        /// Maximum number of retries allowed before stream will fail due to inability to push records.
        /// Default value: 5
        /// </summary>
        public int MaxRetries { get; }

        /// <summary>
        /// Initial delay used before the attempt to redeliver failed put records.
        /// For <see cref="RetryBackoffStrategy.Linear"/> it stays the same.
        /// For <see cref="RetryBackoffStrategy.Exponential"/> it will grow exponentially (factor: 2).
        /// Default value: 500ms
        /// </summary>
        public TimeSpan RetryInitialTimeout { get; }

        public KinesisFlowSettings(
            int parallelism,
            int maxBatchSize,
            RetryBackoffStrategy backoffStrategy,
            TimeSpan retryInitialTimeout,
            int maxRecordsPerSecond,
            int maxBytesPerSecond,
            int maxRetries)
        {
            Parallelism = parallelism;
            MaxBatchSize = maxBatchSize;
            BackoffStrategy = backoffStrategy;
            MaxRecordsPerSecond = maxRecordsPerSecond;
            MaxBytesPerSecond = maxBytesPerSecond;
            MaxRetries = maxRetries;
            RetryInitialTimeout = retryInitialTimeout;
        }

        public KinesisFlowSettings WithParallelism(int parallelism) => new KinesisFlowSettings(
            parallelism: parallelism,
            maxBatchSize: this.MaxBatchSize,
            backoffStrategy: this.BackoffStrategy,
            retryInitialTimeout: this.RetryInitialTimeout,
            maxRecordsPerSecond: this.MaxRecordsPerSecond,
            maxBytesPerSecond: this.MaxBytesPerSecond,
            maxRetries: this.MaxRetries);

        public KinesisFlowSettings WithMaxBatchSize(int maxBatchSize) => new KinesisFlowSettings(
            parallelism: this.Parallelism,
            maxBatchSize: maxBatchSize,
            backoffStrategy: this.BackoffStrategy,
            retryInitialTimeout: this.RetryInitialTimeout,
            maxRecordsPerSecond: this.MaxRecordsPerSecond,
            maxBytesPerSecond: this.MaxBytesPerSecond,
            maxRetries: this.MaxRetries);

        public KinesisFlowSettings WithBackoffStrategy(RetryBackoffStrategy backoffStrategy) => new KinesisFlowSettings(
            parallelism: this.Parallelism,
            maxBatchSize: this.MaxBatchSize,
            backoffStrategy: backoffStrategy,
            retryInitialTimeout: this.RetryInitialTimeout,
            maxRecordsPerSecond: this.MaxRecordsPerSecond,
            maxBytesPerSecond: this.MaxBytesPerSecond,
            maxRetries: this.MaxRetries);

        public KinesisFlowSettings WithPRetryInitialTimeout(TimeSpan retryInitialTimeout) => new KinesisFlowSettings(
            parallelism: this.Parallelism,
            maxBatchSize: this.MaxBatchSize,
            backoffStrategy: this.BackoffStrategy,
            retryInitialTimeout: retryInitialTimeout,
            maxRecordsPerSecond: this.MaxRecordsPerSecond,
            maxBytesPerSecond: this.MaxBytesPerSecond,
            maxRetries: this.MaxRetries);

        public KinesisFlowSettings WithMaxRecordsPerSecond(int maxRecordsPerSecond) => new KinesisFlowSettings(
            parallelism: this.Parallelism,
            maxBatchSize: this.MaxBatchSize,
            backoffStrategy: this.BackoffStrategy,
            retryInitialTimeout: this.RetryInitialTimeout,
            maxRecordsPerSecond: maxRecordsPerSecond,
            maxBytesPerSecond: this.MaxBytesPerSecond,
            maxRetries: this.MaxRetries);

        public KinesisFlowSettings WithMaxBytesPerSecond(int maxBytesPerSecond) => new KinesisFlowSettings(
            parallelism: this.Parallelism,
            maxBatchSize: this.MaxBatchSize,
            backoffStrategy: this.BackoffStrategy,
            retryInitialTimeout: this.RetryInitialTimeout,
            maxRecordsPerSecond: this.MaxRecordsPerSecond,
            maxBytesPerSecond: maxBytesPerSecond,
            maxRetries: this.MaxRetries);

        public KinesisFlowSettings WithMaxRetries(int maxRetries) => new KinesisFlowSettings(
            parallelism: this.Parallelism,
            maxBatchSize: this.MaxBatchSize,
            backoffStrategy: this.BackoffStrategy,
            retryInitialTimeout: this.RetryInitialTimeout,
            maxRecordsPerSecond: this.MaxRecordsPerSecond,
            maxBytesPerSecond: this.MaxBytesPerSecond,
            maxRetries: maxRetries);
    }
}