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
        var sender = Client.CreateSender("queue.1");
        var receiver = Client.CreateReceiver("queue.1");

        await sender.SendMessageAsync(new ServiceBusMessage("Test1"));
        
        var probe = ServiceBusSource.Create(receiver, pollInterval: TimeSpan.FromSeconds(1))
            .Take(3)
            .Select(x => Encoding.UTF8.GetString(x.Body))
            .RunWith(this.SinkProbe<string>(), Materializer);

        probe.Request(2)
            .ExpectNext("Test1")
            .ExpectNoMsg(TimeSpan.FromSeconds(3));

        await sender.SendMessageAsync(new ServiceBusMessage("Test2"));
        await sender.SendMessageAsync(new ServiceBusMessage("Test3"));

        probe.ExpectNext("Test2", TimeSpan.FromSeconds(2));
        probe.Request(1).ExpectNext("Test3").ExpectComplete();
    }

    [Fact(Skip = "Peek does not work, check to see if this is a bug or emulator limitation")]
    public async Task A_QueueSource_should_only_poll_if_demand_is_available()
    {
        var sender = Client.CreateSender("queue.1");
        var receiver = Client.CreateReceiver("queue.1");

        await sender.SendMessageAsync(new ServiceBusMessage("Test1"));

        var probe = ServiceBusSource.Create(receiver, pollInterval: TimeSpan.FromSeconds(1))
            .SelectAsync(1, async x =>
            {
                await receiver.CompleteMessageAsync(x);
                return Encoding.UTF8.GetString(x.Body);
            })
            .RunWith(this.SinkProbe<string>(), Materializer);

        probe.Request(1).ExpectNext("Test1");

        await sender.SendMessageAsync(new ServiceBusMessage("Test2"));

        probe.ExpectNoMsg(TimeSpan.FromSeconds(3));
        //Message wouldn't be visible if the source has called GetMessages even if the message wasn't pushed to the stream
        var queued = await receiver.PeekMessagesAsync(2);
        Assert.True(queued.Count > 0);
        Assert.Equal("Test2", Encoding.UTF8.GetString(queued[0].Body));

        probe.Request(1).ExpectNext("Test2");
    }
}