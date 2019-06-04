#region copyright

//-----------------------------------------------------------------------
// <copyright file="SqsAckFlowTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2019-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions.Extensions;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SQS.Tests
{
    public class SqsAckFlowTests : Akka.TestKit.Xunit2.TestKit
    {
        public const string TestQueueUrl = "https://sqs.eu-west-1.amazonaws.com/0123456789/test-queue";

        private readonly ActorMaterializer materializer;
        private readonly IAmazonSQS client;

        public SqsAckFlowTests(ITestOutputHelper output) : base(output: output)
        {
            this.materializer = Sys.Materializer();
            this.client = Substitute.For<IAmazonSQS>();
        }

        private static Message Message(string recipientHandle) =>
            new Message
            {
                Body = Guid.NewGuid().ToString("N"),
                ReceiptHandle = recipientHandle
            };

        [Fact]
        public void SqsAckFlow_default_stream_should_react_on_Delete_commands()
        {
            client.DeleteMessageAsync(Arg.Any<DeleteMessageRequest>()).Returns(
                Task.FromResult(new DeleteMessageResponse()));

            var publisher = this.CreatePublisherProbe<MessageAction>();
            var subscriber = this.CreateSubscriberProbe<ISqsAckResult>();
            Source.FromPublisher(publisher)
                .Via(SqsAckFlow.Default(client, TestQueueUrl, SqsAckSettings.Default.WithMaxInFlight(1)))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(3);

            publisher.SendNext(MessageAction.Delete(Message("a-1")));
            publisher.SendNext(MessageAction.Delete(Message("a-2")));

            subscriber.ExpectNext<SqsDeleteResult>(ack => ack.Action.Message.ReceiptHandle == "a-1");
            subscriber.ExpectNext<SqsDeleteResult>(ack => ack.Action.Message.ReceiptHandle == "a-2");

            subscriber.Cancel();
        }

        [Fact]
        public void SqsAckFlow_default_stream_should_react_on_ChangeVisibility_commands()
        {
            client.ChangeMessageVisibilityAsync(Arg.Any<ChangeMessageVisibilityRequest>()).Returns(
                Task.FromResult(new ChangeMessageVisibilityResponse()));

            var publisher = this.CreatePublisherProbe<MessageAction>();
            var subscriber = this.CreateSubscriberProbe<ISqsAckResult>();
            Source.FromPublisher(publisher)
                .Via(SqsAckFlow.Default(client, TestQueueUrl, SqsAckSettings.Default.WithMaxInFlight(1)))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(3);

            publisher.SendNext(MessageAction.ChangeVisibility(Message("a-1"), 10.Seconds()));
            publisher.SendNext(MessageAction.ChangeVisibility(Message("a-2"), 13.Seconds()));

            subscriber.ExpectNext<SqsChangeMessageVisibilityResult>(ack =>
                ack.Action.Message.ReceiptHandle == "a-1" && ack.Action.VisibilityTimeout == 10.Seconds());
            subscriber.ExpectNext<SqsChangeMessageVisibilityResult>(ack =>
                ack.Action.Message.ReceiptHandle == "a-2" && ack.Action.VisibilityTimeout == 13.Seconds());

            subscriber.Cancel();
        }

        [Fact]
        public void SqsAckFlow_default_stream_should_react_on_Ignore_commands()
        {
            var publisher = this.CreatePublisherProbe<MessageAction>();
            var subscriber = this.CreateSubscriberProbe<ISqsAckResult>();
            Source.FromPublisher(publisher)
                .Via(SqsAckFlow.Default(client, TestQueueUrl, SqsAckSettings.Default.WithMaxInFlight(1)))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(3);

            publisher.SendNext(MessageAction.Ignore(Message("a-1")));
            publisher.SendNext(MessageAction.Ignore(Message("a-2")));

            subscriber.ExpectNext<SqsIgnoreResult>(ack => ack.Action.Message.ReceiptHandle == "a-1");
            subscriber.ExpectNext<SqsIgnoreResult>(ack => ack.Action.Message.ReceiptHandle == "a-2");

            subscriber.Cancel();
        }

        [Fact]
        public void SqsAckFlow_grouped_stream_should_react_on_Delete_commands()
        {
            client.DeleteMessageBatchAsync(Arg.Any<DeleteMessageBatchRequest>()).Returns(req =>
                Task.FromResult(new DeleteMessageBatchResponse
                {
                    Failed = new List<BatchResultErrorEntry>(0),
                    Successful = ((DeleteMessageBatchRequest) req[0]).Entries
                        .Select(e => new DeleteMessageBatchResultEntry
                        {
                            Id = e.Id
                        }).ToList()
                }));

            var publisher = this.CreatePublisherProbe<MessageAction>();
            var subscriber = this.CreateSubscriberProbe<ISqsAckResultEntry>();
            Source.FromPublisher(publisher)
                .Via(SqsAckFlow.Grouped(client, TestQueueUrl, SqsAckGroupedSettings.Default))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(3);

            publisher.SendNext(MessageAction.Delete(Message("a-1")));
            publisher.SendNext(MessageAction.Delete(Message("a-2")));

            subscriber.ExpectNext<SqsDeleteResult>(ack => ack.Action.Message.ReceiptHandle == "a-1");
            subscriber.ExpectNext<SqsDeleteResult>(ack => ack.Action.Message.ReceiptHandle == "a-2");

            subscriber.Cancel();
        }

        [Fact]
        public void SqsAckFlow_grouped_stream_should_react_on_ChangeVisibility_commands()
        {
            client.ChangeMessageVisibilityBatchAsync(Arg.Any<ChangeMessageVisibilityBatchRequest>()).Returns(req =>
                Task.FromResult(new ChangeMessageVisibilityBatchResponse
                {
                    Failed = new List<BatchResultErrorEntry>(0),
                    Successful = ((ChangeMessageVisibilityBatchRequest) req[0]).Entries
                        .Select(e => new ChangeMessageVisibilityBatchResultEntry
                        {
                            Id = e.Id
                        }).ToList()
                }));
            
            var publisher = this.CreatePublisherProbe<MessageAction>();
            var subscriber = this.CreateSubscriberProbe<ISqsAckResultEntry>();
            Source.FromPublisher(publisher)
                .Via(SqsAckFlow.Grouped(client, TestQueueUrl))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(3);

            publisher.SendNext(MessageAction.ChangeVisibility(Message("a-1"), 10.Seconds()));
            publisher.SendNext(MessageAction.ChangeVisibility(Message("a-2"), 13.Seconds()));

            subscriber.ExpectNext<SqsChangeMessageVisibilityResult>(ack =>
                ack.Action.Message.ReceiptHandle == "a-1" && ack.Action.VisibilityTimeout == 10.Seconds());
            subscriber.ExpectNext<SqsChangeMessageVisibilityResult>(ack =>
                ack.Action.Message.ReceiptHandle == "a-2" && ack.Action.VisibilityTimeout == 13.Seconds());

            subscriber.Cancel();
        }

        [Fact]
        public void SqsAckFlow_grouped_stream_should_react_on_Ignore_commands()
        {
            var publisher = this.CreatePublisherProbe<MessageAction>();
            var subscriber = this.CreateSubscriberProbe<ISqsAckResultEntry>();
            Source.FromPublisher(publisher)
                .Via(SqsAckFlow.Grouped(client, TestQueueUrl))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            subscriber.Request(3);

            publisher.SendNext(MessageAction.Ignore(Message("a-1")));
            publisher.SendNext(MessageAction.Ignore(Message("a-2")));

            subscriber.ExpectNext<SqsIgnoreResult>(ack => ack.Action.Message.ReceiptHandle == "a-1");
            subscriber.ExpectNext<SqsIgnoreResult>(ack => ack.Action.Message.ReceiptHandle == "a-2");

            subscriber.Cancel();
        }
    }
}