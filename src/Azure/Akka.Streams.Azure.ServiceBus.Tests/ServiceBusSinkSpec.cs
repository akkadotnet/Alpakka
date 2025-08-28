// //-----------------------------------------------------------------------
// // <copyright file="ServiceBusSinkSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Text;
using Akka.Configuration;
using Akka.Streams.Azure.ServiceBus;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Azure.Messaging.ServiceBus;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.ServiceBus.Tests;

public class ServiceBusSinkSpec: ServiceBusSpecBase
{
    public ServiceBusSinkSpec(ITestOutputHelper output) : base(Config.Empty, nameof(ServiceBusSinkSpec), output)
    {
    }

    [Fact]
    public async Task A_QueueSink_should_add_elements_to_the_queue()
    {
        var sender = Client.CreateSender("queue.1");
        var receiver = Client.CreateReceiver("queue.1");
        
        var messages = new[] {"1", "2"};
        var t = Source.From(messages)
            .Select(x => new ServiceBusMessage(x))
            .Grouped(10)
            .ToServiceBus(sender, Materializer);

        await t.WaitAsync(15.Seconds());

        var result = await receiver.ReceiveMessagesAsync(10, 15.Seconds());
        Assert.Equal(messages, result.Select(m => Encoding.UTF8.GetString(m.Body)).ToArray());
    }
    
    [Fact]
    public async Task A_QueueSink_should_set_the_exception_of_the_task_when_an_error_occurs()
    {
        var sender = Client.CreateSender("queue.1");
        var (probe, task) = this.SourceProbe<string>()
            .Select(x => new ServiceBusMessage(x))
            .Grouped(10)
            .ToMaterialized(ServiceBusSink.Create(sender), Keep.Both)
            .Run(Materializer);

        probe.SendError(new Exception("Boom"));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => task);
        Assert.Equal("Boom", ex.Message); 
    }
}