#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsAckSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Amazon.SQS;

namespace Akka.Streams.SQS
{
    public static class SqsAckSink
    {
        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> for ack a single SQS message at a time using a <paramref name="client"/>.
        /// </summary>
        public static Sink<MessageAction, Task> Default(IAmazonSQS client, string queueUrl, SqsAckSettings settings = null) =>
            SqsAckFlow.Default(client, queueUrl, settings)
                .ToMaterialized(Sink.Ignore<ISqsAckResult>(), Keep.Right);
        
        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> for ack grouped SQS messages using a <paramref name="client"/>.
        /// </summary>
        public static Sink<MessageAction, Task> Grouped(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null) =>
            SqsAckFlow.Grouped(client, queueUrl, settings)
                .ToMaterialized(Sink.Ignore<ISqsAckResultEntry>(), Keep.Right);
    }
}