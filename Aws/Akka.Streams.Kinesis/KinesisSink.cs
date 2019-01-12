#region copyright
//-----------------------------------------------------------------------
// <copyright file="KinesisSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.IO;
using Akka.IO;
using Akka.Streams.Dsl;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;

namespace Akka.Streams.Kinesis
{
    /// <summary>
    /// A container for factory methods used to build Akka.NET Streams sinks to an Amazon Kinesis streams.
    /// Sinks can be used to send data to Kinesis streams with no acknowledgements - if you need confirmations,
    /// use <see cref="KinesisFlow"/> methods. Sinks carry about retries and rate limiting necessary
    /// to fit into AWS Kinesis constraints.
    /// </summary>
    public static class KinesisSink
    {
        /// <summary>
        /// Creates a default sink used to send raw records to Amazon Kinesis.
        /// </summary>
        /// <param name="streamName">Name of a stream. It must be present before using it.</param>
        /// <param name="settings"></param>
        /// <param name="client">
        /// Amazon Kinesis client factory. After materialization, current Akka.NET Stream will take
        /// responsibility for managing that client, disposing it once stream will be stopped.
        /// </param>
        /// <returns></returns>
        public static Sink<PutRecordsRequestEntry, NotUsed>
            Create(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null) =>
            KinesisFlow.Create(streamName, settings, client).To(Sink.Ignore<PutRecordsResultEntry>());

        /// <summary>
        /// Creates a sink that produces and inserts records to Amazon Kinsesis for a given (partition-key, message-payload) pair.
        /// </summary>
        /// <param name="streamName">Name of a stream. It must be present before using it.</param>
        /// <param name="settings"></param>
        /// <param name="client">
        /// Amazon Kinesis client factory. After materialization, current Akka.NET Stream will take
        /// responsibility for managing that client, disposing it once stream will be stopped.
        /// </param>
        /// <returns></returns>
        public static Sink<(string, ByteString), NotUsed>
            ByPartitionAndBytes(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null) =>
            KinesisFlow.ByPartitionAndBytes(streamName, settings, client).To(Sink.Ignore<PutRecordsResultEntry>());

        /// <summary>
        /// Creates a sink that produces and inserts records to Amazon Kinsesis for a given (partition-key, message-payload) pair.
        /// </summary>
        /// <param name="streamName">Name of a stream. It must be present before using it.</param>
        /// <param name="settings"></param>
        /// <param name="client">
        /// Amazon Kinesis client factory. After materialization, current Akka.NET Stream will take
        /// responsibility for managing that client, disposing it once stream will be stopped.
        /// </param>
        /// <returns></returns>
        public static Sink<(string, MemoryStream), NotUsed>
            ByPartitionAndData(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null) =>
            KinesisFlow.ByPartitionAndData(streamName, settings, client).To(Sink.Ignore<PutRecordsResultEntry>());
    }
}