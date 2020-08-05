using System;
using System.Linq;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using FluentAssertions;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    [Collection("StorageQueueSpec")]
    public class QueueSinkSpec : QueueSpecBase
    {
        private readonly AzureFixture _fixture;
        public QueueSinkSpec(AzureFixture fixture, ITestOutputHelper output) : base(fixture, output)
        {
            _fixture = fixture;
        }

        [Fact]
        public void A_QueueSink_should_add_elements_to_the_queue()
        {
            var messages = new[] {"1", "2"};
            var t = Source.From(messages)
                .Select(x => new CloudQueueMessage(x))
                .ToStorageQueue(Queue, Materializer);

            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            Queue.GetMessages(2).Select(x => x.AsString).Should().BeEquivalentTo(messages);
        }

        [Fact]
        public void A_QueueSink_should_set_the_exception_of_the_task_when_an_error_occurs()
        {
            var t = this.SourceProbe<string>()
                .Select(x => new CloudQueueMessage(x))
                .ToMaterialized(QueueSink.Create(Queue), Keep.Both)
                .Run(Materializer);
            var probe = t.Item1;
            var task = t.Item2;

            probe.SendError(new Exception("Boom"));
            task.Invoking(x => x.Wait(TimeSpan.FromSeconds(3))).Should().Throw<Exception>().WithMessage("Boom");
        }

        [Fact]
        public void A_QueueSink_should_retry_failing_messages_if_supervision_strategy_is_resume()
        {
            Queue.DeleteIfExists();
            var messages = new[] { "1", "2" };
            var queueSink = QueueSink.Create(Queue)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider));

            var t = Source.From(messages)
                .Select(x => new CloudQueueMessage(x))
                .RunWith(queueSink, Materializer);

            Thread.Sleep(1000);
            Queue.Create();
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            Queue.GetMessages(2).Select(x => x.AsString).Should().BeEquivalentTo(messages);
        }

        [Fact]
        public void A_QueueSink_should_skip_failing_messages_if_supervision_strategy_is_restart()
        {
            Queue.DeleteIfExists();
            var queueSink = QueueSink.Create(Queue)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider));

            var t = this.SourceProbe<string>()
                .Select(x => new CloudQueueMessage(x))
                .ToMaterialized(queueSink, Keep.Both)
                .Run(Materializer);

            var probe = t.Item1;
            var task = t.Item2;
            
            probe.SendNext("1");
            Thread.Sleep(500);
            Queue.Create();
            probe.SendNext("2");
            probe.SendComplete();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            Queue.GetMessage().AsString.Should().Be("2");
        }
    }
}
