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
using System.Linq;

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
            var response = CreatePublishResponse("message-id");
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();
            snsService.PublishAsync(request).Returns(response);
            var val = TestSource.SourceProbe<string>(this).Via(SnsPublisher.PublishToSNSFlow("topic-Arn", snsService)).ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both).Run(this.materializer);
            val.Item1.SendNext("sns-message").SendComplete();
            var task =val.Item2.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            val.Item2.Result.ShouldBeEquivalentTo(ImmutableList.Create(response));
            snsService.Received(1).PublishAsync(request);
        }

        private static PublishResponse CreatePublishResponse(string responseMessage)
        {
            PublishResponse response = new PublishResponse();
            response.MessageId = responseMessage;
            return response;
        }

        [Fact]
        public void ItShouldPublishMultipleMessagesToSNs()
        {
            var responseMessageStrings = Enumerable.Range(0, 3).Select(i => String.Format("message-id-{0}", i));
            var expectedResponseMessages = ImmutableList.CreateRange(responseMessageStrings.Select(s =>CreatePublishResponse(s)));
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();
            snsService.PublishAsync(Arg.Any<PublishRequest>())
                .Returns(
                    Task.FromResult(expectedResponseMessages.First()),
                    expectedResponseMessages.Skip(1).Select(t => Task.FromResult(t)).ToArray());
            var val = TestSource.SourceProbe<string>(this).Via(SnsPublisher.PublishToSNSFlow("topic-Arn", snsService)).ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both).Run(this.materializer);
            foreach (var rms in responseMessageStrings)
            {
                val.Item1.SendNext(rms);
            }
            val.Item1.SendComplete();
            var task =val.Item2.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            val.Item2.Result.ShouldBeEquivalentTo(expectedResponseMessages);
            snsService.ReceivedWithAnyArgs(3);
        }
            
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            materializer.Dispose();
        }
        }
}