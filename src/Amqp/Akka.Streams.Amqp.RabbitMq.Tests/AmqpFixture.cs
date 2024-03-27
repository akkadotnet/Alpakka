//-----------------------------------------------------------------------
// <copyright file="AmqpFixture.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Testcontainers.RabbitMq;
using Xunit;

namespace Akka.Streams.Amqp.Tests;

[CollectionDefinition("AmqpSpec")]
public sealed class AmqpSpecFixture : ICollectionFixture<AmqpFixture>
{
}

public class AmqpFixture : IAsyncLifetime
{
    protected readonly string RabbitContainerName = $"rabbit-{Guid.NewGuid():N}";
    public string ConnectionString { get; set; }
    public string HostName => Container!.Hostname;
    public int AmqpPort => Container!.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

    public string UserName => "guest";
    public string Password => "guest";

    public RabbitMqContainer? Container { get; protected set; }

    public async Task InitializeAsync()
    {
        await using var outputConsumer = new OutputConsumer();
        
        Container = new RabbitMqBuilder()
            .WithName(RabbitContainerName)
            .WithUsername(UserName)
            .WithPassword(Password)
            .WithOutputConsumer(outputConsumer)
            .Build();

        await Container.StartAsync();
        
        await outputConsumer.WaitUntilReadyAsync("Server startup complete", 1, TimeSpan.FromMinutes(1));

        ConnectionString = Container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (Container != null) await Container.StopAsync();
    }
}