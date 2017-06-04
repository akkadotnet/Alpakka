using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Akka.Streams.SNS;
using System;
using System.Threading.Tasks;
using FluentAssertions;
using System.Collections.Immutable;

namespace Akka.Streams.SignalR.Tests
{
    public class SnsPublishFlowSpec: Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer materializer;

        public SnsPublishFlowSpec(ITestOutputHelper output)
            : base(output: output)
        {
            materializer = Sys.Materializer();
        }

        [Fact]
        public void ItShouldSendASingleMessageToSNS()
        {
            PublishRequest request = new PublishRequest("topic-arn", "sns-message");
            PublishResponse response = new PublishResponse();
            response.MessageId = "message-id";
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();
            snsService.PublishAsync(request).Returns(response);
            var val = TestSource.SourceProbe<string>(this).Via(SnsPublisher.PublishToSNSFlow("topic-Arn", snsService)).ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both).Run(this.materializer);
            val.Item1.SendNext("sns-message").SendComplete();
            var task =val.Item2.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            val.Item2.Result.ShouldBeEquivalentTo(ImmutableList.Create(response));
        }
        
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            materializer.Dispose();
        }
        }
}