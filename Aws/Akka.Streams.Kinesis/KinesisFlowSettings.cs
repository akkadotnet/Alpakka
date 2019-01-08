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
    public enum RetryBackoffStrategy
    {
        Exponential,
        Linear
    }

    public sealed class KinesisFlowSettings
    {
        private const int MAX_RECORDS_PER_REQUEST = 500;
        private const int MAX_RECORDS_PER_SHARD_PER_SECOND = 1000;
        private const int MAX_BYTES_PER_SHARD_PER_SECOND = 1000000;

        public static KinesisFlowSettings Default = ByNumberOfShard(1);

        private static KinesisFlowSettings ByNumberOfShard(int shards) => new KinesisFlowSettings(
            parallelism: shards * (MAX_RECORDS_PER_SHARD_PER_SECOND / MAX_RECORDS_PER_REQUEST),
            maxBatchSize: MAX_RECORDS_PER_REQUEST,
            backoffStrategy: RetryBackoffStrategy.Exponential,
            retryInitialTimeout: TimeSpan.FromMilliseconds(500),
            maxRecordsPerSecond: shards * MAX_RECORDS_PER_SHARD_PER_SECOND,
            maxBytesPerSecond: shards * MAX_BYTES_PER_SHARD_PER_SECOND,
            maxRetries: 5);

        public int Parallelism { get; }
        public int MaxBatchSize { get; }
        public RetryBackoffStrategy BackoffStrategy { get; }
        public int MaxRecordsPerSecond { get; }
        public int MaxBytesPerSecond { get; }
        public int MaxRetries { get; }
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