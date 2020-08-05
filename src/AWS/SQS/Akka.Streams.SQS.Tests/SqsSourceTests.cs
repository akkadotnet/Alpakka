#region copyright
//-----------------------------------------------------------------------
// <copyright file="SqsSourceTests.cs" company="Akka.NET Project">
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
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SQS.Tests
{
    public class SqsSourceTests : Akka.TestKit.Xunit2.TestKit
    {
        public const string TestQueueUrl = "https://sqs.eu-west-1.amazonaws.com/0123456789/test-queue";
        
        private readonly ActorMaterializer materializer;
        private readonly IAmazonSQS client;
        
        public SqsSourceTests(ITestOutputHelper output) : base(output: output)
        {
            this.materializer = Sys.Materializer();
            this.client = Substitute.For<IAmazonSQS>();
        }

        private ReceiveMessageResponse CreateReceiveMessageResponse(int size) => new ReceiveMessageResponse
        {
            Messages = size == 0 ? new List<Message>() : Enumerable.Range(0, size)
                .Select(i => new Message {Body = i.ToString()})
                .ToList()
        };

        [Fact]
        public void SqsSource_should_return_events()
        {
            var probe = this.CreateManualSubscriberProbe<Message>();
            client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>())
                .Returns(Task.FromResult(CreateReceiveMessageResponse(10)));

            SqsSource.Create(this.client, TestQueueUrl)
                .To(Sink.FromSubscriber(probe))
                .Run(materializer);

            var sub = probe.ExpectSubscription();
            sub.Request(22);
            for (int i = 0; i < 22; i++)
            {
                probe.ExpectNext((Message msg) => msg.Body == (i % 10).ToString());
            }
            sub.Cancel();
        }
        
        [Fact]
        public void SqsSource_when_closeOnEmptyReceive_should_complete_after_returning_empty_result()
        {
            var probe = this.CreateManualSubscriberProbe<Message>();
            client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>())
                .Returns(
                    Task.FromResult(CreateReceiveMessageResponse(2)),
                    Task.FromResult(CreateReceiveMessageResponse(0)),
                    Task.FromResult(CreateReceiveMessageResponse(2)));

            SqsSource.Create(this.client, TestQueueUrl, SqsSourceSettings.Default.WithCloseOnEmptyReceive(true))
                .To(Sink.FromSubscriber(probe))
                .Run(materializer);

            var sub = probe.ExpectSubscription();
            sub.Request(3);
            probe.ExpectNext((Message msg) => msg.Body == "0");
            probe.ExpectNext((Message msg) => msg.Body == "1");
            probe.ExpectComplete();
        }
        
        [Fact]
        public void SqsSource_when_NOT_closeOnEmptyReceive_should_continue_after_returning_empty_result()
        {
            var probe = this.CreateManualSubscriberProbe<Message>();
            client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>())
                .Returns(
                    Task.FromResult(CreateReceiveMessageResponse(2)),
                    Task.FromResult(CreateReceiveMessageResponse(0)),
                    Task.FromResult(CreateReceiveMessageResponse(2)));

            SqsSource.Create(this.client, TestQueueUrl, SqsSourceSettings.Default.WithCloseOnEmptyReceive(false))
                .To(Sink.FromSubscriber(probe))
                .Run(materializer);

            var sub = probe.ExpectSubscription();
            sub.Request(3);
            probe.ExpectNext((Message msg) => msg.Body == "0");
            probe.ExpectNext((Message msg) => msg.Body == "1");
            probe.ExpectNext((Message msg) => msg.Body == "0");
            sub.Request(2);
            probe.ExpectNext((Message msg) => msg.Body == "1");
            sub.Cancel();
        }
    }
}