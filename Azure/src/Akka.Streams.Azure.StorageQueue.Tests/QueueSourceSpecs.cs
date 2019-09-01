using System;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using FluentAssertions;
using Microsoft.Azure.Storage.Queue;
using Xunit;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public class QueueSourceSpecs : QueueSpecBase
    {
        [Fact]
        public void A_QueueSource_should_push_available_messages()
        {
            Queue.AddMessage(new CloudQueueMessage("Test1"));
            Queue.AddMessage(new CloudQueueMessage("Test2"));
            Queue.AddMessage(new CloudQueueMessage("Test3"));
            
            QueueSource.Create(Queue)
                .Take(3)
                .Select(x => x.AsString)
                .RunWith(this.SinkProbe<string>(), Materializer)
                .Request(3)
                .ExpectNext("Test1", "Test2", "Test3")
                .ExpectComplete();
        }

        [Fact]
        public void A_QueueSource_should_poll_for_messages_if_the_queue_is_empty()
        {
            Queue.AddMessage(new CloudQueueMessage("Test1"));
            
            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.AsString)
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(2)
                .ExpectNext("Test1")
                .ExpectNoMsg(TimeSpan.FromSeconds(3));

            Queue.AddMessage(new CloudQueueMessage("Test2"));
            Queue.AddMessage(new CloudQueueMessage("Test3"));

            probe.ExpectNext("Test2", TimeSpan.FromSeconds(2));
            probe.Request(1).ExpectNext("Test3").ExpectComplete();
        }

        [Fact]
        public void A_QueueSource_should_only_poll_if_demand_is_available()
        {
            Queue.AddMessage(new CloudQueueMessage("Test1"));

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Select(x =>
                {
                    Queue.DeleteMessage(x);
                    return x.AsString;
                })
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(1).ExpectNext("Test1");

            Queue.AddMessage(new CloudQueueMessage("Test2"));

            probe.ExpectNoMsg(TimeSpan.FromSeconds(3));
            //Message wouldn't be visible if the source has called GetMessages even if the message wasn't pushed to the stream
            Queue.PeekMessage().AsString.Should().Be("Test2");
            
            probe.Request(1).ExpectNext("Test2");
        }

        [Fact]
        public void A_QueueSource_should_fail_when_an_error_occurs()
        {
            Queue.DeleteIfExists();

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.AsString)
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(1).ExpectError();
        }

        [Fact]
        public void A_QueueSource_should_not_fail_if_the_supervision_strategy_is_not_stop_when_an_error_occurs()
        {
            Queue.DeleteIfExists();

            var probe = QueueSource.Create(Queue, pollInterval: TimeSpan.FromSeconds(1))
                .Take(3)
                .Select(x => x.AsString)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(this.SinkProbe<string>(), Materializer);

            probe.Request(3).ExpectNoMsg();

            Queue.CreateIfNotExists();
            Queue.AddMessage(new CloudQueueMessage("Test1"));
            Queue.AddMessage(new CloudQueueMessage("Test2"));
            Queue.AddMessage(new CloudQueueMessage("Test3"));

            probe.ExpectNext("Test1", "Test2", "Test3")
                .ExpectComplete();
        }
    }
}
