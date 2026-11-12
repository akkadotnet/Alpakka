//-----------------------------------------------------------------------
// <copyright file="AmqpFixture.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using System.Threading;
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
    public string? ConnectionString { get; private set; }
    public string HostName => Container!.Hostname;
    public int AmqpPort => Container!.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

    public string UserName => "guest";
    public string Password => "guest";

    public RabbitMqContainer? Container { get; protected set; }

    public async Task InitializeAsync()
    {
        Container = new RabbitMqBuilder("rabbitmq:4.4.0")
            .WithName(RabbitContainerName)
            .WithUsername(UserName)
            .WithPassword(Password)
            // Retain coverage of the connector's existing non-durable, non-exclusive
            // queue defaults. RabbitMQ 4.3+ requires explicitly enabling that feature.
            .WithResourceMapping(Encoding.UTF8.GetBytes(
                    "deprecated_features.permit.transient_nonexcl_queues = true\n"),
                "/etc/rabbitmq/conf.d/20-alpakka-compat.conf")
            .Build();

        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await Container.StartAsync(startup.Token);
            ConnectionString = Container.GetConnectionString();
        }
        catch (Exception startupException)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupException)
            {
                throw new AggregateException("RabbitMQ startup and cleanup both failed.", startupException, cleanupException);
            }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var container = Container;
        Container = null;
        if (container != null)
        {
            await container.DisposeAsync();
        }
    }
}
