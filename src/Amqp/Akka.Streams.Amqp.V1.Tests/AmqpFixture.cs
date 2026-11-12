using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Amqp.V1.Tests;
using Testcontainers.ActiveMq;
using Xunit;

#nullable enable
namespace Akka.Streams.Amqp.Tests;

[CollectionDefinition("AmqpSpec")]
public sealed class AmqpSpecFixture : ICollectionFixture<AmqpFixture>
{
}

public class AmqpFixture : IAsyncLifetime
{
    protected readonly string BrokerContainerName = $"alpakka-amqp-v1-{Guid.NewGuid():N}";
    public string? ConnectionString { get; private set; }
    public string HostName => Container!.Hostname;
    public int AmqpPort => Container!.GetMappedPublicPort(ArtemisBuilder.ArtemisMainPort);

    public string UserName => "guest";
    public string Password => "guest";

    public ArtemisContainer? Container { get; protected set; }

    public async Task InitializeAsync()
    {
        Container = new ArtemisBuilder("apache/activemq-artemis:2.42.0")
            .WithName(BrokerContainerName)
            .WithUsername(UserName)
            .WithPassword(Password)
            .Build();
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Container.StartAsync(startup.Token);
            ConnectionString = new UriBuilder("amqp", HostName, AmqpPort)
            {
                UserName = UserName,
                Password = Password
            }.Uri.ToString();
        }
        catch (Exception startupException)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("AMQP broker startup and cleanup both failed.", startupException, cleanupException);
            }

            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var container = Container;
        Container = null;
        ConnectionString = null;
        if (container is not null)
            await container.DisposeAsync();
    }
}
