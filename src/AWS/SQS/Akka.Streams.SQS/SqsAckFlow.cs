#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsAckFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Akka.Streams.SQS
{
    public static class SqsAckFlow
    {
        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> for ack a single SQS message
        /// at a time using a <paramref name="client"/>.
        /// </summary>
        public static Flow<MessageAction, ISqsAckResult, NotUsed> Default(IAmazonSQS client, string queueUrl,
            SqsAckSettings settings = null)
        {
            settings = settings ?? SqsAckSettings.Default;

            return Flow.Create<MessageAction>()
                .SelectAsync(settings.MaxInFlight, async action =>
                {
                    switch (action)
                    {
                        case Delete delete:
                        {
                            var request = new DeleteMessageRequest(queueUrl, delete.Message.ReceiptHandle);
                            var response = await client.DeleteMessageAsync(request);
                            return (ISqsAckResult)new SqsDeleteResult(delete, response);
                        }

                        case ChangeMessageVisibility changeVisibility:
                        {
                            var request = new ChangeMessageVisibilityRequest(queueUrl, changeVisibility.Message.ReceiptHandle, (int)changeVisibility.VisibilityTimeout.TotalSeconds);
                            var response = await client.ChangeMessageVisibilityAsync(request);
                            return new SqsChangeMessageVisibilityResult(changeVisibility, response);
                        }
                        case Ignore ignore: return new SqsIgnoreResult(ignore);
                        default:
                            throw new NotSupportedException($"Message of type [{action.GetType()}] is not supported by the Akka.Streams.SQS.SqsAckFlow.Default method.");
                    }
                });
        }

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> for ack grouped SQS messages using a <paramref name="client"/>.
        /// </summary>
        public static Flow<MessageAction, ISqsAckResultEntry, NotUsed> Grouped(IAmazonSQS client, string queueUrl,
            SqsAckGroupedSettings settings = null)
        {
            settings = settings ?? SqsAckGroupedSettings.Default;

            return Flow.FromGraph(GraphDsl.Create(builder =>
            {
                var p = builder.Add(new Partition<MessageAction>(3, action =>
                {
                    switch (action)
                    {
                        case Delete _: return 0;
                        case ChangeMessageVisibility _: return 1;
                        case Ignore _: return 2;
                        default: throw new NotSupportedException($"Message of type [{action.GetType()}] is not supported by the Akka.Streams.SQS.SqsAckFlow.Grouped method.");
                    }
                }));

                var merge = builder.Add(new Merge<ISqsAckResultEntry>(3));

                var delete = builder.Add(Flow.Create<MessageAction>().Collect(a => a as Delete));
                var changeMessageVisibility = builder.Add(Flow.Create<MessageAction>().Collect(a => a as ChangeMessageVisibility));
                var ignore = builder.Add(Flow.Create<MessageAction>().Collect(a => a as Ignore));

                builder.From(p.Out(0)).Via(delete).Via(GroupedDelete(client, queueUrl, settings).Select(x => (ISqsAckResultEntry)x)).To(merge.In(0));
                builder.From(p.Out(1)).Via(changeMessageVisibility).Via(GroupedChangeMessageVisibility(client, queueUrl, settings).Select(x => (ISqsAckResultEntry)x)).To(merge.In(1));
                builder.From(p.Out(2)).Via(ignore).Via(Flow.Create<Ignore>().Select(action => (ISqsAckResultEntry)new SqsIgnoreResultEntry(action))).To(merge.In(2));

                return new FlowShape<MessageAction, ISqsAckResultEntry>(p.In, merge.Out);
            }));
        }
        
        public static Flow<Delete, SqsDeleteResultEntry, NotUsed> GroupedDelete(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null)
        {
            settings = settings ?? SqsAckGroupedSettings.Default;

            return Flow.Create<Delete>()
                .GroupedWithin(settings.MaxBatchSize, settings.MaxBatchWait)
                .Select(requests =>
                {
                    var entries = new List<DeleteMessageBatchRequestEntry>();
                    var actions = new List<Delete>();
                    var i = 0;
                    foreach (var request in requests)
                    {
                        actions.Add(request);
                        entries.Add(new DeleteMessageBatchRequestEntry(i.ToString(), request.Message.ReceiptHandle));
                        i++;
                    }

                    return Tuple.Create(actions, new DeleteMessageBatchRequest(queueUrl, entries));
                })
                .SelectAsync(settings.ConcurrentRequests, async tuple =>
                {
                    var actions = tuple.Item1;
                    var request = tuple.Item2;

                    var response = await client.DeleteMessageBatchAsync(request);

                    if (response.Failed.Count == 0)
                    {
                        var responseMetadata = response.ResponseMetadata;
                        var resultEntries = response.Successful.ToDictionary(e => int.Parse(e.Id), e => e);
                        var results = new List<SqsDeleteResultEntry>(request.Entries.Count);
                        var i = 0;
                        foreach (var action in actions)
                        {
                            var result = resultEntries[i];
                            results.Add(new SqsDeleteResultEntry(action, result, responseMetadata));
                            i++;
                        }

                        return results;
                    }
                    else
                    {
                        var numberOfMessages = request.Entries.Count;
                        var nrOfFailedMessages = response.Failed.Count;
                        
                        throw new SqsBatchException($"Some messages are failed to delete. {nrOfFailedMessages} of {numberOfMessages} messages are failed");
                    }
                })
                .SelectMany(x => x);
        }

        public static Flow<ChangeMessageVisibility, SqsChangeMessageVisibilityResultEntry, NotUsed> GroupedChangeMessageVisibility(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null)
        {
            settings = settings ?? SqsAckGroupedSettings.Default;

            return Flow.Create<ChangeMessageVisibility>()
                .GroupedWithin(settings.MaxBatchSize, settings.MaxBatchWait)
                .Select(requests =>
                {
                    var entries = new List<ChangeMessageVisibilityBatchRequestEntry>();
                    var actions = new List<ChangeMessageVisibility>();
                    var i = 0;
                    foreach (var request in requests)
                    {
                        actions.Add(request);
                        entries.Add(new ChangeMessageVisibilityBatchRequestEntry(i.ToString(), request.Message.ReceiptHandle)
                        {
                            VisibilityTimeout = (int)request.VisibilityTimeout.TotalSeconds
                        });
                        i++;
                    }

                    return Tuple.Create(actions, new ChangeMessageVisibilityBatchRequest(queueUrl, entries));
                })
                .SelectAsync(settings.ConcurrentRequests, async tuple =>
                {
                    var actions = tuple.Item1;
                    var request = tuple.Item2;

                    var response = await client.ChangeMessageVisibilityBatchAsync(request);

                    if (response.Failed.Count == 0)
                    {
                        var responseMetadata = response.ResponseMetadata;
                        var resultEntries = response.Successful.ToDictionary(e => int.Parse(e.Id), e => e);
                        var results = new List<SqsChangeMessageVisibilityResultEntry>(request.Entries.Count);
                        var i = 0;
                        foreach (var action in actions)
                        {
                            var result = resultEntries[i];
                            results.Add(new SqsChangeMessageVisibilityResultEntry(action, result, responseMetadata));
                            i++;
                        }

                        return results;
                    }
                    else
                    {
                        var numberOfMessages = request.Entries.Count;
                        var nrOfFailedMessages = response.Failed.Count;
                        
                        throw new SqsBatchException($"Some messages are failed to delete. {nrOfFailedMessages} of {numberOfMessages} messages are failed");
                    }
                })
                .SelectMany(x => x);
        }

    }
}