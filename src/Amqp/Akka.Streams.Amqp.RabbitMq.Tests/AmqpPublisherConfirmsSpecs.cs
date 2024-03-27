//-----------------------------------------------------------------------
// <copyright file="AmqpPublisherConfirmsSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Amqp.RabbitMq;
using Akka.Streams.Amqp.RabbitMq.Dsl;
using Akka.Streams.Dsl;
using FluentAssertions;
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
    public async Task AmqpPublisherConfirms_should_publish_messages_with_confirms()
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
        await Source.From(messages)
            .Select(ByteString.FromString)
            .RunWith(amqpSink, _mat)
            .WaitAsync(RemainingOrDefault);
    }
}