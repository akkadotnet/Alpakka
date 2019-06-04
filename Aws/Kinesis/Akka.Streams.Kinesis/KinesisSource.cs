#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using Akka.Streams.Dsl;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;

namespace Akka.Streams.Kinesis
{
    /// <summary>
    /// A container class for factory methods used to build Akka.NET Streams sources to an Amazon Kinesis streams.
    /// This provider is realized by periodically fetching the data, accordingly to settings specified inside
    /// <see cref="ShardSettings"/> configuration.
    /// </summary>
    public static class KinesisSource
    {
        /// <summary>
        /// Creates a basic source that will allow to connect to a single AWS Kinesis stream and shard.
        /// To combine sources targetting multiple shards, use <see cref="SourceOperations.Merge{TOut1,TOut2,TMat}"/> method.
        ///
        /// All requests will be send via provided Amazon Kinesis <paramref name="client"/>. After materialization,
        /// current Akka.NET Stream will take full responsibility for managing that client, disposing it
        /// once stream will be stopped.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="client"></param>
        /// <returns></returns>
        public static Source<Record, NotUsed> Basic(ShardSettings settings, Func<IAmazonKinesis> client = null) =>
            Source.FromGraph(new KinesisSourceStage(settings, client ?? KinesisFlow.DefaultClientFactory));
    }
}