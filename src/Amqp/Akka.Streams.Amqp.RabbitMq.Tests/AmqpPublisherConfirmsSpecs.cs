//-----------------------------------------------------------------------
// <copyright file="AmqpPublisherConfirmsSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;
using FluentAssertions;
using Nito.AsyncEx;
using Xunit;
using Xunit.Abstractions;

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
        task.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
#pragma warning restore xUnit1031
        return Task.CompletedTask;
#endif
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
        task.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
#pragma warning restore xUnit1031
        return Task.CompletedTask;
#endif
    }
}