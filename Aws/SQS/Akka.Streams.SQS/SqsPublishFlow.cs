#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsPublishSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Akka.Streams.SQS
{
    /// <summary>
    /// API to create publishing SQS flows.
    /// </summary>
    public static class SqsPublishFlow
    {
        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> to publish messages to a SQS queue using an <paramref name="client"/>.
        /// </summary>
        public static Flow<SendMessageRequest, SqsPublishResult, NotUsed> Default(IAmazonSQS client, string queueUrl,
            SqsPublishSettings settings = null)
        {
            settings = settings ?? SqsPublishSettings.Default;

            return Flow.Create<SendMessageRequest>()
                .SelectAsync(settings.MaxInFlight, async req => Tuple.Create(req, await client.SendMessageAsync(req)))
                .Select(tuple => new SqsPublishResult(tuple.Item1, tuple.Item2));
        }

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> to publish messages to a SQS queue using a <paramref name="client"/>.
        /// See also: https://getakka.net/articles/streams/builtinstages.html#groupedwithin
        /// </summary>
        public static Flow<SendMessageRequest, SqsPublishResultEntry, NotUsed> Grouped(IAmazonSQS client,
            string queueUrl, SqsPublishGroupedSettings settings = null)
        {
            settings = settings ?? SqsPublishGroupedSettings.Default;
            
            return Flow.Create<SendMessageRequest>()
                .GroupedWithin(settings.MaxBatchSize, settings.MaxBatchWait)
                .Via(Batch(client, queueUrl, SqsPublishBatchSettings.Default.WithConcurrentRequests(settings.ConcurrentRequests)))
                .SelectMany(x => x);
        }

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> to publish messages in batches to a SQS queue using a <paramref name="client"/>.
        /// </summary>
        public static Flow<IEnumerable<SendMessageRequest>, IReadOnlyList<SqsPublishResultEntry>, NotUsed> Batch(
            IAmazonSQS client, string queueUrl, SqsPublishBatchSettings settings = null)
        {
            settings = settings ?? SqsPublishBatchSettings.Default;

            return Flow.Create<IEnumerable<SendMessageRequest>>()
                .Select(requests =>
                {
                    var batch = new List<SendMessageRequest>();
                    var entries = new List<SendMessageBatchRequestEntry>();
                    var i = 0;
                    foreach (var request in requests)
                    {
                        batch.Add(request);
                        entries.Add(new SendMessageBatchRequestEntry(i.ToString(), request.MessageBody)
                        {
                            MessageDeduplicationId = request.MessageDeduplicationId,
                            MessageGroupId = request.MessageGroupId,
                            MessageAttributes = request.MessageAttributes
                        });

                        i++;
                    }

                    return Tuple.Create(batch, new SendMessageBatchRequest(queueUrl, entries));
                })
                .SelectAsync(settings.ConcurrentRequests, async tuple =>
                {
                    var requests = tuple.Item1;
                    var batchRequest = tuple.Item2;
                    var response = await client.SendMessageBatchAsync(batchRequest);
                    if (response.Failed.Count == 0)
                    {
                        var responseMetadata = response.ResponseMetadata;
                        var resultEntries = response.Successful.ToDictionary(e => int.Parse(e.Id), e => e);

                        var results = new List<SqsPublishResultEntry>(requests.Count);
                        var i = 0;
                        foreach (var request in requests)
                        {
                            var result = resultEntries[i];
                            results.Add(new SqsPublishResultEntry(request, result, responseMetadata));
                        }

                        return (IReadOnlyList<SqsPublishResultEntry>)results;
                    }
                    else
                    {
                        var numberOfMessages = batchRequest.Entries.Count;
                        var nrOfFailedMessages = response.Failed.Count;
                        
                        throw new SqsBatchException($"Some messages are failed to send. {nrOfFailedMessages} of {numberOfMessages} messages are failed");
                    }
                });
        }
    }
}