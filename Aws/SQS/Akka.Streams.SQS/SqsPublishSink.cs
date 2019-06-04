#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsPublishSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Akka.Streams.SQS
{
    /// <summary>
    /// Scala API to create publishing SQS sinks.
    /// </summary>
    public static class SqsPublishSink
    {
        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that accepts strings and publishes them as messages
        /// to a SQS queue using a <paramref name="client"/>.
        /// </summary>
        public static Sink<string, Task> Default(IAmazonSQS client, string queueUrl, SqsPublishSettings settings = null) =>
            Flow.FromFunction((string msg) => new SendMessageRequest(queueUrl, msg))
                .ToMaterialized(MessageSink(client, queueUrl, settings), Keep.Right);

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that accepts strings and publishes them as messages
        /// to a SQS queue using a <paramref name="client"/>.
        /// </summary>
        private static Sink<SendMessageRequest, Task> MessageSink(IAmazonSQS client, string queueUrl, SqsPublishSettings settings = null) =>
            SqsPublishFlow.Default(client, queueUrl, settings)
                .ToMaterialized(Sink.Ignore<SqsPublishResult>(), Keep.Right);

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that groups strings and publishes
        /// them as messages in batches to a SQS queue using a <paramref name="client"/>.
        /// See also: https://getakka.net/articles/streams/builtinstages.html#groupedwithin
        /// </summary>
        public static Sink<string, Task> Grouped(IAmazonSQS client, string queueUrl, SqsPublishGroupedSettings settings = null) =>
            Flow.FromFunction((string msg) => new SendMessageRequest(queueUrl, msg))
                .ToMaterialized(GroupedMessageSink(client, queueUrl, settings), Keep.Right);

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that groups messages and publishes
        /// them in batches to a SQS queue using a <paramref name="client"/>.
        /// See also: https://getakka.net/articles/streams/builtinstages.html#groupedwithin
        /// </summary>
        public static Sink<SendMessageRequest, Task> GroupedMessageSink(IAmazonSQS client, string queueUrl, SqsPublishGroupedSettings settings = null) =>
            SqsPublishFlow.Grouped(client, queueUrl, settings)
                .ToMaterialized(Sink.Ignore<SqsPublishResultEntry>(), Keep.Right);

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that accepts an iterable of strings and publish
        /// them as messages in batches to a SQS queue using a <paramref name="client"/>.
        /// See also: https://getakka.net/articles/streams/builtinstages.html#groupedwithin
        /// </summary>
        public static Sink<IEnumerable<string>, Task> Batch(IAmazonSQS client, string queueUrl,
            SqsPublishBatchSettings settings = null) =>
            Flow.FromFunction((IEnumerable<string> msgs) => 
                    msgs.Select(msg => new SendMessageRequest(queueUrl, msg)))
                .ToMaterialized(BatchedMessageSink(client, queueUrl, settings), Keep.Right);

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> to publish messages in batches
        /// to a SQS queue using a <paramref name="client"/>.
        /// See also: https://getakka.net/articles/streams/builtinstages.html#groupedwithin
        /// </summary>
        public static Sink<IEnumerable<SendMessageRequest>, Task> BatchedMessageSink(IAmazonSQS client, string queueUrl, SqsPublishBatchSettings settings = null) =>
            SqsPublishFlow.Batch(client, queueUrl, settings)
                .ToMaterialized(Sink.Ignore<IReadOnlyList<SqsPublishResultEntry>>(), Keep.Right);
    }
}