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
    /// <summary>
    /// A container for factory methods used to build Akka.NET Streams flows to an Amazon Kinesis streams.
    /// Flows can be used to send data to Kinesis streams. They handle acknowledgments, retries and rate
    /// limiting necessary to fit into AWS Kinesis constraints.
    /// </summary>
    public static class KinesisFlow
    {
        private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
        internal static readonly Func<IAmazonKinesis> DefaultClientFactory = () => new AmazonKinesisClient();

        /// <summary>
        /// Creates a default flow used to send raw records to Amazon Kinesis.
        /// </summary>
        /// <param name="streamName">Name of a stream. It must be present before using it.</param>
        /// <param name="settings"></param>
        /// <param name="client">
        /// Amazon Kinesis client factory. After materialization, current Akka.NET Stream will take
        /// responsibility for managing that client, disposing it once stream will be stopped.
        /// </param>
        /// <returns></returns>
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

        /// <summary>
        /// Creates a flow that produces and inserts records to Amazon Kinsesis for a given (partition-key, message-payload) pair.
        /// </summary>
        /// <param name="streamName">Name of a stream. It must be present before using it.</param>
        /// <param name="settings"></param>
        /// <param name="client">
        /// Amazon Kinesis client factory. After materialization, current Akka.NET Stream will take
        /// responsibility for managing that client, disposing it once stream will be stopped.
        /// </param>
        /// <returns></returns>
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

        /// <summary>
        /// Creates a flow that produces and inserts records to Amazon Kinsesis for a given (partition-key, message-payload) pair.
        /// </summary>
        /// <param name="streamName">Name of a stream. It must be present before using it.</param>
        /// <param name="settings"></param>
        /// <param name="client">
        /// Amazon Kinesis client factory. After materialization, current Akka.NET Stream will take
        /// responsibility for managing that client, disposing it once stream will be stopped.
        /// </param>
        /// <returns></returns>
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