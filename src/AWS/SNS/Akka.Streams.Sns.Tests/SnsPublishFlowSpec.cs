using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
        public async Task ItShouldPublishMultipleMessagesToSns()
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
            await AwaitAssertAsync(() => Assert.True(val.Item2.IsCompleted), TimeSpan.FromSeconds(1));
            (await val.Item2).Should().BeEquivalentTo(expectedResponseMessages);
            await snsService.Received(3).PublishAsync(Arg.Any<PublishRequest>());
        }

        [Fact]
        public async Task ItShouldFailTheStageIfTheAmazonSnsAsyncClientRequestFails()
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
            await AwaitAssertAsync(() => Assert.True(task.IsCompleted), TimeSpan.FromSeconds(1));
            var error = await Assert.ThrowsAsync<AmazonSimpleNotificationServiceException>(() => task);
            error.Message.Should().Be("test");
            await snsService.Received(1).PublishAsync(Arg.Any<PublishRequest>());
        }

        [Fact]
        public async Task ItShouldFailTheStageIfAnUpstreamFailureOccurs()
        {
            var snsService = Substitute.For<IAmazonSimpleNotificationService>();

            var (probe, task) = this.SourceProbe<string>()
                .Via(SnsPublisher.PlainFlow("topic-arn", snsService))
                .ToMaterialized(Sink.Seq<PublishResponse>(), Keep.Both)
                .Run(_materializer);

            probe.SendError(new Exception("upstream failure"));

            await AwaitAssertAsync(() => Assert.True(task.IsCompleted), TimeSpan.FromSeconds(1));
            var error = await Assert.ThrowsAsync<Exception>(() => task);
            error.Message.Should().Be("upstream failure");
            await snsService.DidNotReceive().PublishAsync(Arg.Any<PublishRequest>());
        }

        private static PublishResponse CreatePublishResponse(string responseMessage)
        {
            var response = new PublishResponse {MessageId = responseMessage};
            return response;
        }

        [Fact]
        public async Task The_final_response_completes_without_extra_downstream_demand()
        {
            var response = new PublishResponse { MessageId = "last-id" };
            var pending = new TaskCompletionSource<PublishResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = Substitute.For<IAmazonSimpleNotificationService>();
            client.PublishAsync(Arg.Any<PublishRequest>()).Returns(_ =>
            {
                started.TrySetResult(true);
                return pending.Task;
            });
            var probe = Source.Single("last").Via(SnsPublisher.PlainFlow("topic", client))
                .RunWith(this.SinkProbe<PublishResponse>(), _materializer);
            probe.EnsureSubscription().Request(1);
            try
            {
                await AwaitAssertAsync(() => Assert.True(started.Task.IsCompleted));
                pending.SetResult(response);
                probe.ExpectNext(response).ExpectComplete();
            }
            finally { probe.Cancel(); }
        }

        [Fact]
        public void A_canceled_sdk_publish_fails_with_a_cancellation_error()
        {
            var client = Substitute.For<IAmazonSimpleNotificationService>();
            client.PublishAsync(Arg.Any<PublishRequest>()).Returns(
                Task.FromCanceled<PublishResponse>(new CancellationToken(canceled: true)));
            var probe = Source.Single("canceled").Via(SnsPublisher.PlainFlow("topic", client))
                .RunWith(this.SinkProbe<PublishResponse>(), _materializer);
            probe.EnsureSubscription().Request(1);
            try { Assert.IsType<TaskCanceledException>(probe.ExpectError()); }
            finally { probe.Cancel(); }
        }

        protected override void Dispose(bool disposing)
        {
            this._materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}
