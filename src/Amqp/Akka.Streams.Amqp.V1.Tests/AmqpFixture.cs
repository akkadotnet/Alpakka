using System;
using System.Threading.Tasks;
using Akka.Streams.Amqp.V1.Tests;
using Testcontainers.RabbitMq;
using Xunit;

#nullable enable
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
        using var outputConsumer = new OutputConsumer();
        
        Container = new RabbitMqBuilder()
            .WithImage("akkadotnet/rabbitmq-linux")
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
        if (Container != null)
        {
            await Container.StopAsync();
            await Container.DisposeAsync();
        }
    }
}