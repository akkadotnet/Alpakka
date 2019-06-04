#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion
using System.Linq;
using Akka.Streams.Dsl;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Akka.Streams.SQS
{
    /// <summary>
    /// API to create SQS sources.
    /// </summary>
    public static class SqsSource
    {
        public static Source<Message, NotUsed> Create(IAmazonSQS client, string queueUrl, SqsSourceSettings settings = null)
        {
            settings = settings ?? SqsSourceSettings.Default;
            var request = new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = settings.MaxBatchSize,
                WaitTimeSeconds = (int)settings.WaitTime.TotalSeconds,
                AttributeNames = settings.AttributeNames.Select(a => a.Name).ToList(),
                MessageAttributeNames = settings.MessageAttributeNames.Select(a => a.Name).ToList()
            };
            
            if (settings.VisibilityTimeout.HasValue)
                request.VisibilityTimeout = (int)settings.VisibilityTimeout.Value.TotalSeconds;

            return Source.Repeat(request)
                .SelectAsync(settings.Parallelism, req => client.ReceiveMessageAsync(req))
                .TakeWhile(resp => !settings.CloseOnEmptyReceive || resp.Messages.Count != 0)
                .SelectMany(resp => resp.Messages)
                .Buffer(settings.MaxBufferSize, OverflowStrategy.Backpressure);
        }
    }
}