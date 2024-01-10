using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using FluentAssertions;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using FluentAssertions.Extensions;
using static FluentAssertions.FluentActions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public class QueueSinkSpec : QueueSpecBase
    {
        public QueueSinkSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task A_QueueSink_should_add_elements_to_the_queue()
        {
            var messages = new[] {"1", "2"};
            var t = Source.From(messages)
                //.Select(x => new QueueMessage(x))
                .ToStorageQueue(Queue, Materializer);
            await Awaiting(() => t).Should().CompleteWithinAsync(15.Seconds());
            var result = await Awaiting(() => Queue.ReceiveMessagesAsync(2))
                .Should().CompleteWithinAsync(15.Seconds());
            result.Subject.Value.Select(x => x.MessageText).Should().BeEquivalentTo(messages);
        }

        [Fact]
        public async Task A_QueueSink_should_set_the_exception_of_the_task_when_an_error_occurs()
        {
            var (probe, task) = this.SourceProbe<string>()
                //.Select(x => new QueueMessage(x))
                .ToMaterialized(QueueSink.Create(Queue), Keep.Both)
                .Run(Materializer);

            probe.SendError(new Exception("Boom"));
            await Awaiting(() => task).Should().ThrowAsync<Exception>().WithMessage("Boom");
        }

        [Fact]
        public async Task A_QueueSink_should_retry_failing_messages_if_supervision_strategy_is_resume()
        {
            await Queue.DeleteIfExistsAsync();
            var messages = new[] { "1", "2" };
            var queueSink = QueueSink.Create(Queue)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider));

            var t = Source.From(messages)
                //.Select(x => new QueueMessage(x))
                .RunWith(queueSink, Materializer);

            await Task.Delay(1000);
            await Queue.CreateAsync();

            await Awaiting(() => t).Should().CompleteWithinAsync(15.Seconds());
            var result = await Awaiting(() => Queue.ReceiveMessagesAsync(2))
                .Should().CompleteWithinAsync(15.Seconds());
            result.Subject.Value.Select(x => x.MessageText).Should().BeEquivalentTo(messages);
        }

        [Fact]
        public async Task A_QueueSink_should_skip_failing_messages_if_supervision_strategy_is_restart()
        {
            await Queue.DeleteIfExistsAsync();
            var queueSink = QueueSink.Create(Queue)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider));

            var t = this.SourceProbe<string>()
                //.Select(x => new QueueMessage(x))
                .ToMaterialized(queueSink, Keep.Both)
                .Run(Materializer);

            var probe = t.Item1;
            var task = t.Item2;
            
            probe.SendNext("1");
            await Task.Delay(500);
            await Queue.CreateAsync();
            probe.SendNext("2");
            probe.SendComplete();
            await task;
            var msg = (await Queue.ReceiveMessagesAsync()).Value;
            Assert.NotEmpty(msg);
            msg[0].MessageText.Should().Be("2");
        }
    }
}
