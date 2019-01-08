#region copyright
//-----------------------------------------------------------------------
// <copyright file="ShardSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using Akka.Streams.Dsl;
using Amazon.Kinesis;

namespace Akka.Streams.Kinesis
{
    /// <summary>
    /// Immutable settings class used to configure <see cref="KinesisSource"/>.
    /// </summary>
    public sealed class ShardSettings
    {
        /// <summary>
        /// Creates a new instance of <see cref="ShardSettings"/> class.
        /// </summary>
        /// <param name="streamName">Name of Amazon Kinesis stream. It must be already present.</param>
        /// <param name="shardId">Name of an Amazon Kinesis shard in context of stream with provided <paramref name="streamName"/>.</param>
        /// <returns></returns>
        public static ShardSettings Create(string streamName, string shardId) =>
            new ShardSettings(streamName, shardId, ShardIteratorType.LATEST, TimeSpan.FromSeconds(1), 500, null, null);

        /// <summary>
        /// Name of a stream to start receiving data from.
        /// </summary>
        public string StreamName { get; }
        
        /// <summary>
        /// Shard identifier for a given stream. To merge data from multiple kinesis shards,
        /// you can use <see cref="SourceOperations.Merge{TOut1,TOut2,TMat}"/> method.
        /// </summary>
        public string ShardId { get; }

        /// <summary>
        /// Type of shard iterator defined for Amazon Kinesis client.
        /// Default value: <see cref="ShardIteratorType.LATEST"/>
        /// </summary>
        public ShardIteratorType ShardIteratorType { get; }

        /// <summary>
        /// <see cref="KinesisSource"/> is polling Amazon Kinesis accordingly to a given interval.
        /// Default value: 1 second.
        /// </summary>
        public TimeSpan RefreshInterval { get; }

        /// <summary>
        /// <see cref="KinesisSource"/> is polling Amazon Kinesis in batches. This value must be between 0 and 10 000.
        /// Default value: 500
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// A starting sequence number, from which the source should continue fetching the data.
        /// Default value: null
        /// </summary>
        public string StartingSequenceNumber { get; }
        public DateTime? AtTimestamp { get; }

        public ShardSettings(
            string streamName, 
            string shardId, 
            ShardIteratorType shardIteratorType,
            TimeSpan refreshInterval,
            int limit,
            string startingSequenceNumber = null,
            DateTime? atTimestamp = null)
        {
            if (limit > 10000 || limit < 1)
                throw new ArgumentException("Limit must be between 0 and 10000. See: http://docs.aws.amazon.com/kinesis/latest/APIReference/API_GetRecords.html");

            StreamName = streamName;
            ShardId = shardId;
            ShardIteratorType = shardIteratorType;
            RefreshInterval = refreshInterval;
            Limit = limit;
            StartingSequenceNumber = startingSequenceNumber;
            AtTimestamp = atTimestamp;

            if (shardIteratorType == ShardIteratorType.AFTER_SEQUENCE_NUMBER ||
                shardIteratorType == ShardIteratorType.AT_SEQUENCE_NUMBER)
            {
                if (string.IsNullOrEmpty(startingSequenceNumber))
                    throw new ArgumentException($"Shard iterator type [{shardIteratorType}] require starting sequence number to be provided.", nameof(startingSequenceNumber));
            }
            else if (shardIteratorType == ShardIteratorType.AT_TIMESTAMP)
            {
                if (!atTimestamp.HasValue) 
                    throw new ArgumentException($"Shard iterator type [{shardIteratorType}] require atTimestamp to be provided.", nameof(atTimestamp));
            }
        }

        public ShardSettings WithShardIteratorType(ShardIteratorType shardIteratorType) =>
            new ShardSettings(this.StreamName, this.ShardId, shardIteratorType, this.RefreshInterval, this.Limit, this.StartingSequenceNumber, this.AtTimestamp);

        public ShardSettings WithStartingSequenceNumber(string startingSequenceNumber) =>
            new ShardSettings(this.StreamName, this.ShardId, this.ShardIteratorType, this.RefreshInterval, this.Limit, startingSequenceNumber, this.AtTimestamp);

        public ShardSettings WithAtTimestamp(DateTime atTimestamp) =>
            new ShardSettings(this.StreamName, this.ShardId, this.ShardIteratorType, this.RefreshInterval, this.Limit, this.StartingSequenceNumber, atTimestamp);

        public ShardSettings WithRefreshInterval(TimeSpan refreshInterval) =>
            new ShardSettings(this.StreamName, this.ShardId, this.ShardIteratorType, refreshInterval, this.Limit, this.StartingSequenceNumber, this.AtTimestamp);

        public ShardSettings WithLimit(int limit) =>
            new ShardSettings(this.StreamName, this.ShardId, this.ShardIteratorType, this.RefreshInterval, limit, this.StartingSequenceNumber, this.AtTimestamp);
    }
}