using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Sns.Tests
{
    public class SnsPublishFlowSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;

        public SnsPublishFlowSpec(ITestOutputHelper output)
            : base(output: output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public async Task ItShouldPublishASingleMessageToSns()
        {
            var publishRequest = new PublishRequest("topic-arn", "sns-message");
            var publishResult = new PublishResponse { MessageId = "message-id" };

            var snsService = Substitute.For<IAmazonSimpleNotificationService>();
            snsService.PublishAsync(Arg.Any<PublishRequest>())
                .Returns(publishResult);

            var (probe, task) = this.SourceProbe<string>()
                .Via(SnsPublisher.PlainFlow("topic-arn", snsService))
                .ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both)
                .Run(this._materializer);

            probe.SendNext("sns-message").SendComplete();
            var actualResult = (await task).FirstOrDefault();
            actualResult.Should().Be(publishResult);
            await snsService.Received(1).PublishAsync(Arg.Any<PublishRequest>());
        }

        [Fact]
        public void ItShouldPublishMultipleMessagesToSns()
        {
            var responseMessageStrings = Enumerable.Range(0, 3).Select(i => String.Format("message-id-{0}", i));
            var expectedResponseMessages = ImmutableList.CreateRange(responseMessageStrings.Select(s => CreatePublishResponse(s)));
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();
            snsService.PublishAsync(Arg.Any<PublishRequest>())
                .Returns(
                    Task.FromResult(expectedResponseMessages.First()),
                    expectedResponseMessages.Skip(1).Select(t => Task.FromResult(t)).ToArray());
            var val = TestSource.SourceProbe<string>(this).Via(SnsPublisher.PlainFlow("topic-Arn", snsService)).ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both).Run(this._materializer);
            foreach (var rms in responseMessageStrings)
            {
                val.Item1.SendNext(rms);
            }
            val.Item1.SendComplete();
            var task = val.Item2.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            val.Item2.Result.Should().BeEquivalentTo(expectedResponseMessages);
            snsService.ReceivedWithAnyArgs(3);
        }

        [Fact]
        public void ItShouldFailTheStageIfTheAmazonSnsAsyncClientRequestFails()
        {
            var request = new PublishRequest("topic-arn", "sns-message");
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();
            snsService.When(x => x.PublishAsync(Arg.Any<PublishRequest>())).Do(x => 
                throw new AmazonSimpleNotificationServiceException("test"));

            var (probe, task) = this.SourceProbe<string>()
                .Via(SnsPublisher.PlainFlow("topic-arn", snsService))
                .ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both)
                .Run(_materializer);

            probe.SendNext("sns-message").SendComplete();
            Action act = () => task.Wait(TimeSpan.FromSeconds(1));
            act.Should().Throw<AmazonSimpleNotificationServiceException>().WithMessage("test");
            snsService.Received(1).PublishAsync(Arg.Any<PublishRequest>());
        }

        [Fact]
        public void ItShouldFailTheStageIfAnUpstreamFailureOccurs()
        {
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();

            var (probe, task) = this.SourceProbe<string>()
                .Via(SnsPublisher.PlainFlow("topic-arn", snsService))
                .ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both)
                .Run(_materializer);

            probe.SendError(new Exception("upstream failure"));

            Action act = () => task.Wait(TimeSpan.FromSeconds(1));
            act.Should().Throw<Exception>().WithMessage("upstream failure");
            snsService.DidNotReceive().PublishAsync(Arg.Any<PublishRequest>());
        }

        private static PublishResponse CreatePublishResponse(string responseMessage)
        {
            var response = new PublishResponse {MessageId = responseMessage};
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            this._materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}