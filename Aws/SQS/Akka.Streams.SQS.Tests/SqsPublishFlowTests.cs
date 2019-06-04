#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsPublishFlowTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SQS.Tests
{
    public class SqsPublishFlowTests : Akka.TestKit.Xunit2.TestKit
    {
        public const string TestQueueUrl = "https://sqs.eu-west-1.amazonaws.com/0123456789/test-queue";

        private readonly ActorMaterializer materializer;
        private readonly IAmazonSQS client;

        public SqsPublishFlowTests(ITestOutputHelper output) : base(output: output)
        {
            this.materializer = Sys.Materializer();
            this.client = Substitute.For<IAmazonSQS>();
        }

        [Fact]
        public void SqsPublishFlow_default_should_send_messages_to_SQS()
        {
            client.SendMessageAsync(Arg.Any<SendMessageRequest>()).Returns(req =>
                Task.FromResult(new SendMessageResponse{}));
            
            var publisher = this.CreatePublisherProbe<string>();
            var subscriber = this.CreateSubscriberProbe<SqsPublishResult>();
            Source.FromPublisher(publisher)
                .Select(msg => new SendMessageRequest(TestQueueUrl, msg))
                .Via(SqsPublishFlow.Default(client, TestQueueUrl, SqsPublishSettings.Default.WithMaxInFlight(1)))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(2);

            publisher.SendNext("a-1");
            publisher.SendNext("a-2");

            subscriber.ExpectNext<SqsPublishResult>(r => r.Request.MessageBody == "a-1" && r.Request.QueueUrl == TestQueueUrl);
            subscriber.ExpectNext<SqsPublishResult>(r => r.Request.MessageBody == "a-2" && r.Request.QueueUrl == TestQueueUrl);

            subscriber.Cancel();
        }
        
        [Fact]
        public void SqsPublishFlow_grouped_should_send_messages_to_SQS_in_batches()
        {
            client.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>()).Returns(req =>
                Task.FromResult(new SendMessageBatchResponse
                {
                    Failed = new List<BatchResultErrorEntry>(0),
                    Successful = ((SendMessageBatchRequest)req[0]).Entries
                        .Select(e => new SendMessageBatchResultEntry
                        {
                            Id = e.Id 
                        }).ToList()
                }));
            
            var publisher = this.CreatePublisherProbe<string>();
            var subscriber = this.CreateSubscriberProbe<SqsPublishResultEntry>();
            Source.FromPublisher(publisher)
                .Select(msg => new SendMessageRequest(TestQueueUrl, msg))
                .Via(SqsPublishFlow.Grouped(client, TestQueueUrl))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(2);

            publisher.SendNext("a-1");
            publisher.SendNext("a-2");

            subscriber.ExpectNext<SqsPublishResultEntry>(r => r.Request.MessageBody == "a-1" && r.Request.QueueUrl == TestQueueUrl);
            subscriber.ExpectNext<SqsPublishResultEntry>(r => r.Request.MessageBody == "a-2" && r.Request.QueueUrl == TestQueueUrl);

            subscriber.Cancel();
        }
        
        [Fact]
        public void SqsPublishFlow_batch_should_send_messages_to_SQS_in_batches()
        {
            client.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>()).Returns(req =>
                Task.FromResult(new SendMessageBatchResponse
                {
                    Failed = new List<BatchResultErrorEntry>(0),
                    Successful = ((SendMessageBatchRequest)req[0]).Entries
                        .Select(e => new SendMessageBatchResultEntry
                        {
                            Id = e.Id 
                        }).ToList()
                }));
            
            var publisher = this.CreatePublisherProbe<string[]>();
            var subscriber = this.CreateSubscriberProbe<string[]>();
            Source.FromPublisher(publisher)
                .Select(msgs => msgs.Select(msg => new SendMessageRequest(TestQueueUrl, msg)))
                .Via(SqsPublishFlow.Batch(client, TestQueueUrl))
                .Select(list => list.Select(x => x.Request.MessageBody).ToArray())
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(2);

            publisher.SendNext(new []{"a-1", "a-2"});
            publisher.SendNext(new []{"b-1", "b-2", "b-3"});

            subscriber.ExpectNext<string[]>(x => x.SequenceEqual(new []{"a-1", "a-2"}));
            subscriber.ExpectNext<string[]>(x => x.SequenceEqual(new []{"b-1", "b-2", "b-3"}));

            subscriber.Cancel();
        }
        
    }
}