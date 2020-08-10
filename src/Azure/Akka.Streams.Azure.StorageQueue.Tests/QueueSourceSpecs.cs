using System;
using System.Threading.Tasks;
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
    public class QueueSourceSpecs : QueueSpecBase
    {
        private readonly AzureFixture _fixture;
        public QueueSourceSpecs(AzureFixture fixture, ITestOutputHelper output) : base(fixture, output)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task A_QueueSource_should_push_available_messages()
        {
            await Queue.AddMessageAsync(new CloudQueueMessage("Test1"));
            await Queue.AddMessageAsync(new CloudQueueMessage("Test2"));
            await Queue.AddMessageAsync(new CloudQueueMessage("Test3"));
            
            QueueSource.Create(Queue)
                .Take(3)
                .Select(x => x.AsString)
                .RunWith(this.SinkProbe<string>(), Materializer)
                .Request(3)
                .ExpectNext("Test1", "Test2", "Test3")
                .ExpectComplete();
        }

        [Fact]
        public async Task A_QueueSource_should_poll_for_messages_if_the_queue_is_empty()
        {
            await Queue.AddMessageAsync(new CloudQueueMessage("Test1"));
            
            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.AsString)
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(2)
                .ExpectNext("Test1")
                .ExpectNoMsg(TimeSpan.FromSeconds(3));

            await Queue.AddMessageAsync(new CloudQueueMessage("Test2"));
            await Queue.AddMessageAsync(new CloudQueueMessage("Test3"));

            probe.ExpectNext("Test2", TimeSpan.FromSeconds(2));
            probe.Request(1).ExpectNext("Test3").ExpectComplete();
        }

        [Fact]
        public async Task A_QueueSource_should_only_poll_if_demand_is_available()
        {
            await Queue.AddMessageAsync(new CloudQueueMessage("Test1"));

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Select(async x =>
                {
                    await Queue.DeleteMessageAsync(x);
                    return x.AsString;
                })
                .RunWith(this.SinkProbe<Task<string>>(), Materializer);

            //probe.Request(1).ExpectNext("Test1");
            (await probe.Request(1).RequestNext()).Should().Be("Test1");

            await Queue.AddMessageAsync(new CloudQueueMessage("Test2"));

            probe.ExpectNoMsg(TimeSpan.FromSeconds(3));
            //Message wouldn't be visible if the source has called GetMessages even if the message wasn't pushed to the stream
            (await Queue.PeekMessageAsync()).AsString.Should().Be("Test2");

            (await probe.Request(1).RequestNext()).Should().Be("Test2");
            //probe.Request(1).ExpectNext("Test2");
        }

        [Fact]
        public async Task A_QueueSource_should_fail_when_an_error_occurs()
        {
            await Queue.DeleteIfExistsAsync();

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.AsString)
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(1).ExpectError();
        }

        [Fact]
        public async Task A_QueueSource_should_not_fail_if_the_supervision_strategy_is_not_stop_when_an_error_occurs()
        {
            await Queue.DeleteIfExistsAsync();

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.AsString)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(3).ExpectNoMsg();

            await Queue.CreateIfNotExistsAsync();
            await Queue.AddMessageAsync(new CloudQueueMessage("Test1"));
            await Queue.AddMessageAsync(new CloudQueueMessage("Test2"));
            await Queue.AddMessageAsync(new CloudQueueMessage("Test3"));

            probe.ExpectNext("Test1", "Test2", "Test3")
                .ExpectComplete();
        }
    }
}
