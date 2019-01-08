#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Immutable;
using System.IO;
using Akka.IO;
using Akka.Streams.Dsl;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Flow = Akka.Streams.Dsl.Flow;

namespace Akka.Streams.Kinesis
{
    public static class KinesisFlow
    {
        private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
        internal static readonly Func<IAmazonKinesis> DefaultClientFactory = () => new AmazonKinesisClient();

        public static Flow<PutRecordsRequestEntry, PutRecordsResultEntry, NotUsed>
            Create(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null)
        {
            settings = settings ?? KinesisFlowSettings.Default;
            client = client ?? DefaultClientFactory;

            return Flow.Create<PutRecordsRequestEntry>()
                .Throttle(settings.MaxRecordsPerSecond, Second, settings.MaxRecordsPerSecond, ThrottleMode.Shaping)
                .Throttle(settings.MaxBytesPerSecond, Second, settings.MaxBytesPerSecond, GetPayloadByteSize, ThrottleMode.Shaping)
                .Batch(settings.MaxBatchSize, ImmutableQueue.Create, (queue, request) => queue.Enqueue(request))
                .Via(new KinesisFlowStage(streamName, settings.MaxRetries, settings.BackoffStrategy, settings.RetryInitialTimeout, client))
                .SelectAsync(settings.Parallelism, task => task)
                .SelectMany(result => result);
        }

        private static int GetPayloadByteSize(PutRecordsRequestEntry request) =>
            request.PartitionKey.Length + (int)request.Data.Position;

        public static Flow<(string, ByteString), PutRecordsResultEntry, NotUsed> 
            ByPartitionAndBytes(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null)
        {
            return Flow.Create<(string, ByteString)>()
                .Select(tuple =>
                {
                    var bytes = tuple.Item2;
                    var stream = new MemoryStream(bytes.Count);
                    bytes.WriteTo(stream);
                    return new PutRecordsRequestEntry
                    {
                        PartitionKey = tuple.Item1,
                        Data = stream
                    };
                })
                .Via(Create(streamName, settings, client));
        }

        public static Flow<(string, MemoryStream), PutRecordsResultEntry, NotUsed>
            ByPartitionAndData(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null)
        {
            return Flow.Create<(string, MemoryStream)>()
                .Select(tuple => new PutRecordsRequestEntry
                {
                    PartitionKey = tuple.Item1,
                    Data = tuple.Item2
                })
                .Via(Create(streamName, settings, client));
        }
    }
}