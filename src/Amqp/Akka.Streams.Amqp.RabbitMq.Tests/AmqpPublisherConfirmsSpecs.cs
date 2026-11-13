//-----------------------------------------------------------------------
// <copyright file="AmqpPublisherConfirmsSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;
using Nito.AsyncEx;
using Xunit;
using Xunit.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Akka.Streams.Amqp.Tests;

[Collection("AmqpSpec")]
public class AmqpPublisherConfirmsSpecs : Akka.TestKit.Xunit2.TestKit
{
    private readonly AmqpConnectionDetails _connectionSettings;
    private readonly ActorMaterializer _mat;
    private readonly AmqpFixture _fixture;
    
    public  AmqpPublisherConfirmsSpecs(AmqpFixture fixture, ITestOutputHelper output) : 
        base(output:output)
    {
        _mat = ActorMaterializer.Create(Sys);
        _connectionSettings =
            AmqpConnectionDetails
                .Create(fixture.HostName, fixture.AmqpPort)
                .WithCredentials(AmqpCredentials.Create(fixture.UserName, fixture.Password))
                .WithAutomaticRecoveryEnabled(true)
                .WithNetworkRecoveryInterval(TimeSpan.FromSeconds(1));
        _fixture = fixture;
    }
    
    [Fact]
    public Task AmqpSink_should_publish_messages_with_confirms()
    {
        var queueName = "amqp-conn-it-spec-simple-queue-" + Environment.TickCount;
        var queueDeclaration = QueueDeclaration.Create(queueName).WithDurable(false).WithAutoDelete(true);

        var amqpSink = AmqpSink.CreateSimple(
            AmqpSinkSettings.Create(_connectionSettings)
                .WithRoutingKey(queueName)
                .WithDeclarations(queueDeclaration)
                .WithWaitForConfirms(TimeSpan.FromSeconds(5))
        );

        var messages = new[] { "one", "two", "three", "four", "five" };
        var task = Source.From(messages)
            .Select(ByteString.FromString)
            .RunWith(amqpSink, _mat);
#if NET6_0_OR_GREATER
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        return task.WaitAsync(cts.Token);
#else
#pragma warning disable xUnit1031
        Assert.True(task.Wait(TimeSpan.FromSeconds(5)));
#pragma warning restore xUnit1031
        return Task.CompletedTask;
#endif
    }

    [Fact]
    public async Task A_blocked_broker_cannot_complete_a_publish_requiring_confirmation()
    {
        await using var broker = new RabbitMqBuilder("rabbitmq:4.4.0")
            .WithUsername("guest").WithPassword("guest").Build();
        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await broker.StartAsync(startup.Token);
        var factory = new ConnectionFactory
        {
            Uri = new Uri(broker.GetConnectionString()),
            ContinuationTimeout = TimeSpan.FromSeconds(2)
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        const string queue = "blocked-confirms";
        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare("alarm-observer", durable: true, exclusive: false, autoDelete: false);
        var blocked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionBlocked += (_, args) => blocked.TrySetResult(args.Reason);
        var alarm = await broker.ExecAsync(new[] { "rabbitmqctl", "set_vm_memory_high_watermark", "absolute", "1" });
        Assert.Equal(0, alarm.ExitCode);
        channel.BasicPublish("", "alarm-observer", basicProperties: null, new byte[] { 1 });
        Assert.Same(blocked.Task, await Task.WhenAny(blocked.Task, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.False(string.IsNullOrWhiteSpace(await blocked.Task));

        var settings = AmqpConnectionDetails.Create(broker.Hostname, broker.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort))
            .WithCredentials(AmqpCredentials.Create("guest", "guest"))
            .WithConnectionTimeout(TimeSpan.FromSeconds(2));
        settings.ClientProvidedName = "alpakka-confirm-control";
        var completion = Source.Single(ByteString.FromString("requires-confirmation"))
            .RunWith(AmqpSink.CreateSimple(AmqpSinkSettings.Create(settings)
                .WithRoutingKey(queue).WithWaitForConfirms(TimeSpan.FromSeconds(30))), _mat);
        using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            Assert.False(completion.IsCompleted, "A blocked broker has not confirmed the publish; the sink must remain pending.");
            var status = await broker.ExecAsync(new[] { "rabbitmqctl", "list_connections", "--quiet", "state", "client_properties" }, observation.Token);
            Assert.Equal(0, status.ExitCode);
            if (status.Stdout.Split('\n').Any(line => line.StartsWith("blocked\t", StringComparison.Ordinal)
                    && line.Contains("alpakka-confirm-control")))
                break;
            await Task.Delay(100, observation.Token);
        }
        Assert.False(completion.IsCompleted);
        var release = await broker.ExecAsync(new[] { "rabbitmqctl", "set_vm_memory_high_watermark", "0.99" });
        Assert.Equal(0, release.ExitCode);
        var finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(completion, finished);
        await completion;
        var received = channel.BasicGet(queue, autoAck: true);
        Assert.NotNull(received);
        Assert.Equal("requires-confirmation", Encoding.UTF8.GetString(received.Body.ToArray()));
        Assert.Null(channel.BasicGet(queue, autoAck: true));
    }
    
    [Fact]
    public Task AmqpFlow_should_publish_messages_with_confirms()
    {
        var queueName = "amqp-conn-it-spec-simple-queue2-" + Environment.TickCount;
        var queueDeclaration = QueueDeclaration.Create(queueName).WithDurable(false).WithAutoDelete(true);

        var amqpFlow = AmqpFlow.Create<string>(
            AmqpSinkSettings.Create(_connectionSettings)
                .WithRoutingKey(queueName)
                .WithDeclarations(queueDeclaration)
                .WithWaitForConfirms(TimeSpan.FromSeconds(5))
        );

        var messages = new[] { "one", "two", "three", "four", "five" };
        var task = Source.From(messages)
            .Select(s => (new OutgoingMessage(ByteString.FromString(s), true, true), s))
            .Via(amqpFlow)
            .RunForeach(c => { }, _mat);
#if NET6_0_OR_GREATER
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        return task.WaitAsync(cts.Token);
#else
#pragma warning disable xUnit1031
        Assert.True(task.Wait(TimeSpan.FromSeconds(5)));
#pragma warning restore xUnit1031
        return Task.CompletedTask;
#endif
    }
}
