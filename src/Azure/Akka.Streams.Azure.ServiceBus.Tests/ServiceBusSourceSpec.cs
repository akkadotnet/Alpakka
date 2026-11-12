// //-----------------------------------------------------------------------
// // <copyright file="ServiceBusSourceSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Text;
using Akka.Configuration;
using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Azure.Messaging.ServiceBus;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.ServiceBus.Tests;

public class ServiceBusSourceSpec: ServiceBusSpecBase
{
    public ServiceBusSourceSpec(ITestOutputHelper output) : base(Config.Empty, nameof(ServiceBusSourceSpec), output)
    {
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Peek_distinguishes_locked_messages_from_completed_messages(bool topic)
    {
        await using var sender = Client.CreateSender(topic ? "orders" : "queue.1");
        await using var receiver = topic
            ? Client.CreateReceiver("orders", "shipping-subscriber")
            : Client.CreateReceiver("queue.1");
        await using var observer = topic
            ? Client.CreateReceiver("orders", "shipping-subscriber")
            : Client.CreateReceiver("queue.1");

        await sender.SendMessageAsync(new ServiceBusMessage("settlement-control"));
        var held = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(held);
        var locked = Assert.Single(await observer.PeekMessagesAsync(1, fromSequenceNumber: 0));
        Assert.Equal(held.SequenceNumber, locked.SequenceNumber);
        Assert.Equal("settlement-control", locked.Body.ToString());

        await receiver.CompleteMessageAsync(held);
        Assert.Empty(await observer.PeekMessagesAsync(1, fromSequenceNumber: 0));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Queue_and_topic_roundtrips_preserve_payloads_and_settle_messages(bool topic, bool automaticSettlement)
    {
        await using var sender = Client.CreateSender(topic ? "orders" : "queue.1");
        await using var receiver = topic
            ? Client.CreateReceiver("orders", "shipping-subscriber")
            : Client.CreateReceiver("queue.1");
        var expected = new[] { "first", "second" };

        await Source.From(expected)
            .Select(value => new ServiceBusMessage(BinaryData.FromObjectAsJson(value)))
            .Grouped(2)
            .RunWith(ServiceBusSink.Create(sender), Materializer)
            .WaitAsync(TimeSpan.FromSeconds(30));

        await AwaitAssertAsync(async () =>
        {
            var stored = await receiver.PeekMessagesAsync(2, fromSequenceNumber: 0);
            Assert.Equal(expected, stored.Select(message => message.Body.ToObjectFromJson<string>()));
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));

        var source = automaticSettlement
            ? ServiceBusSource.Create<string>(receiver, maxMessageCount: 2)
            : ServiceBusSource.Create(receiver, maxMessageCount: 2).SelectAsync(1, async message =>
            {
                var value = message.Body.ToObjectFromJson<string>()!;
                await receiver.CompleteMessageAsync(message);
                return value;
            });
        var received = await source.Take(2).RunWith(Sink.Seq<string>(), Materializer)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(expected, received);
        Assert.False(receiver.IsClosed);
        await using var observer = topic
            ? Client.CreateReceiver("orders", "shipping-subscriber")
            : Client.CreateReceiver("queue.1");
        Assert.Empty(await observer.PeekMessagesAsync(2, fromSequenceNumber: 0));
        Assert.Null(await observer.ReceiveMessageAsync(TimeSpan.FromSeconds(1)));
    }
    
    [Fact]
    public async Task A_QueueSource_should_push_available_messages()
    {
        var sender = Client.CreateSender("queue.1");
        var receiver = Client.CreateReceiver("queue.1");

        await sender.SendMessageAsync(new ServiceBusMessage("Test1"));
        await sender.SendMessageAsync(new ServiceBusMessage("Test2"));
        await sender.SendMessageAsync(new ServiceBusMessage("Test3"));
        
        var probe = ServiceBusSource.Create(receiver)
            .Take(3)
            .Select(x => Encoding.UTF8.GetString(x.Body))
            .RunWith(this.SinkProbe<string>(), Materializer);

        probe.Request(3)
            .ExpectNext("Test1", "Test2", "Test3")
            .ExpectComplete();
    }

    [Fact]
    public async Task A_QueueSource_should_poll_for_messages_if_the_queue_is_empty()
    {
        await using var sender = Client.CreateSender("queue.1");
        await using var receiver = Client.CreateReceiver("queue.1");

        await sender.SendMessageAsync(new ServiceBusMessage("Test1"));
        
        var probe = ServiceBusSource.Create(receiver, pollInterval: TimeSpan.FromSeconds(1))
            .Take(3)
            .Select(x => Encoding.UTF8.GetString(x.Body))
            .RunWith(this.SinkProbe<string>(), Materializer);

        try
        {
            probe.Request(2)
                .ExpectNext("Test1")
                .ExpectNoMsg(TimeSpan.FromSeconds(3));

            await sender.SendMessageAsync(new ServiceBusMessage("Test2"));
            Assert.Equal("Test2", await probe.ExpectNextAsync(TimeSpan.FromSeconds(2), CancellationToken.None));
            await sender.SendMessageAsync(new ServiceBusMessage("Test3"));
            probe.Request(1).ExpectNext("Test3").ExpectComplete();
        }
        finally
        {
            probe.Cancel();
        }
    }

    [Fact]
    public async Task A_QueueSource_should_leave_the_next_message_visible_to_peek()
    {
        var sender = Client.CreateSender("queue.1");
        var receiver = Client.CreateReceiver("queue.1");

        await sender.SendMessageAsync(new ServiceBusMessage("Test1"));

        var probe = ServiceBusSource.Create(receiver, pollInterval: TimeSpan.FromSeconds(1))
            .RunWith(this.SinkProbe<ServiceBusReceivedMessage>(), Materializer);

        probe.Request(1);
        var first = probe.ExpectNext();
        Assert.Equal("Test1", first.Body.ToString());
        await receiver.CompleteMessageAsync(first);

        await sender.SendMessageAsync(new ServiceBusMessage("Test2"));

        probe.ExpectNoMsg(TimeSpan.FromSeconds(3));
        // Receive-call demand is checked independently by ServiceBusSourceContractSpec.
        var queued = await receiver.PeekMessagesAsync(2, fromSequenceNumber: 0);
        Assert.True(queued.Count > 0);
        Assert.Equal("Test2", Encoding.UTF8.GetString(queued[0].Body));

        probe.Request(1);
        Assert.Equal("Test2", probe.ExpectNext().Body.ToString());
        probe.Cancel();
    }
}
