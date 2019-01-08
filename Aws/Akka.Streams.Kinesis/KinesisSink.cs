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
    public static class KinesisSink
    {
        public static Sink<PutRecordsRequestEntry, NotUsed>
            Create(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null) =>
            KinesisFlow.Create(streamName, settings, client).To(Sink.Ignore<PutRecordsResultEntry>());

        public static Sink<(string, ByteString), NotUsed>
            ByPartitionAndBytes(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null) =>
            KinesisFlow.ByPartitionAndBytes(streamName, settings, client).To(Sink.Ignore<PutRecordsResultEntry>());

        public static Sink<(string, MemoryStream), NotUsed>
            ByPartitionAndData(string streamName, KinesisFlowSettings settings = null, Func<IAmazonKinesis> client = null) =>
            KinesisFlow.ByPartitionAndData(streamName, settings, client).To(Sink.Ignore<PutRecordsResultEntry>());
    }
}