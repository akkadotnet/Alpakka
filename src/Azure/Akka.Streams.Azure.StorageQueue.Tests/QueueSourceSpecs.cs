using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using FluentAssertions;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public class QueueSourceSpecs : QueueSpecBase
    {
        public QueueSourceSpecs(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task A_QueueSource_should_push_available_messages()
        {
            await Queue.SendMessageAsync("Test1");
            await Queue.SendMessageAsync("Test2");
            await Queue.SendMessageAsync("Test3");
            
            var probe = QueueSource.Create(Queue)
                .Take(3)
                .Select(x => x.MessageText)
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(3)
                .ExpectNext("Test1", "Test2", "Test3")
                .ExpectComplete();
        }

        [Fact]
        public async Task A_QueueSource_should_poll_for_messages_if_the_queue_is_empty()
        {
            await Queue.SendMessageAsync("Test1");
            
            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.MessageText)
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(2)
                .ExpectNext("Test1")
                .ExpectNoMsg(TimeSpan.FromSeconds(3));

            await Queue.SendMessageAsync("Test2");
            await Queue.SendMessageAsync("Test3");

            probe.ExpectNext("Test2", TimeSpan.FromSeconds(2));
            probe.Request(1).ExpectNext("Test3").ExpectComplete();
        }

        [Fact]
        public async Task A_QueueSource_should_only_poll_if_demand_is_available()
        {
            await Queue.SendMessageAsync("Test1");

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Select(x =>
                {
                    Queue.DeleteMessage(x.MessageId, x.PopReceipt);
                    return x.MessageText;
                })
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(1).ExpectNext("Test1");

            await Queue.SendMessageAsync("Test2");

            probe.ExpectNoMsg(TimeSpan.FromSeconds(3));
            //Message wouldn't be visible if the source has called GetMessages even if the message wasn't pushed to the stream
            (await Queue.PeekMessagesAsync(1)).Value[0].MessageText.Should().Be("Test2");

            probe.Request(1).ExpectNext("Test2");
        }

        [Fact]
        public async Task A_QueueSource_should_fail_when_an_error_occurs()
        {
            await Queue.DeleteIfExistsAsync();

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.MessageText)
                .RunWith(this.SinkProbe<string>(), Materializer);

            Output.WriteLine(probe.Request(1).ExpectError().Message);
        }

        [Fact]
        public async Task A_QueueSource_should_not_fail_if_the_supervision_strategy_is_not_stop_when_an_error_occurs()
        {
            await Queue.DeleteIfExistsAsync();

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.MessageText)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(3).ExpectNoMsg();

            await Queue.CreateIfNotExistsAsync();
            await Queue.SendMessageAsync("Test1", TimeSpan.Zero);
            await Queue.SendMessageAsync("Test2", TimeSpan.Zero);
            await Queue.SendMessageAsync("Test3", TimeSpan.Zero);

            probe.ExpectNext("Test1", "Test2", "Test3")
                .ExpectComplete();
        }
    }
}
