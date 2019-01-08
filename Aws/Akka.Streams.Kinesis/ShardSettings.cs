#region copyright
//-----------------------------------------------------------------------
// <copyright file="ShardSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using Amazon.Kinesis;

namespace Akka.Streams.Kinesis
{
    public sealed class ShardSettings
    {
        public static ShardSettings Create(string streamName, string shardId) =>
            new ShardSettings(streamName, shardId, ShardIteratorType.LATEST, TimeSpan.FromSeconds(1), 500, null, null);

        public string StreamName { get; }
        public string ShardId { get; }
        public ShardIteratorType ShardIteratorType { get; }
        public TimeSpan RefreshInterval { get; }
        public int Limit { get; }
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