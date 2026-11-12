using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.EventHub.Tests;

public abstract class EventHubSpecBase(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output), IAsyncLifetime
{
    private DistributedApplication? _app;
    public string ConnectionString { get; private set; } = null!;
    public BlobContainerClient CheckpointStore { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EventHub_AppHost>(
                ["--Testing=true"], startup.Token);
            _app = await builder.BuildAsync(startup.Token);
            await _app.StartAsync(startup.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("eventhubs", startup.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("storage", startup.Token);
            ConnectionString = await _app.GetConnectionStringAsync("eventhubs", startup.Token)
                ?? throw new InvalidOperationException("Event Hubs connection string was not provided.");
            var storage = await _app.GetConnectionStringAsync("checkpoint", startup.Token)
                ?? throw new InvalidOperationException("Checkpoint storage connection string was not provided.");
            CheckpointStore = new BlobContainerClient(storage, "checkpoints",
                new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_01_03));
            await CheckpointStore.CreateIfNotExistsAsync(cancellationToken: startup.Token);
        }
        catch (Exception startupException)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Event Hubs startup and cleanup both failed.", startupException, cleanupException);
            }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var app = _app;
        _app = null;
        try
        {
            await Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (app is not null)
                await app.DisposeAsync();
        }
    }
}
