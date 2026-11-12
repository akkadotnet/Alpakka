using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.EventHub.Tests;

public abstract class EventHubSpecBase(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output), IAsyncLifetime
{
    private IDistributedApplicationTestingBuilder? _builder;
    private DistributedApplication? _app;
    public string ConnectionString { get; private set; } = null!;
    public BlobContainerClient CheckpointStore { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EventHub_AppHost>(
                ["--Testing=true"], startup.Token);
            _app = await _builder.BuildAsync(startup.Token);
            _builder = null;
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
        var builder = _builder;
        _app = null;
        _builder = null;
        List<Exception> errors = [];
        await CleanupAsync(() => Sys.Terminate(), TimeSpan.FromSeconds(15));
        if (builder is not null)
            await CleanupAsync(() => builder.DisposeAsync().AsTask(), TimeSpan.FromSeconds(30));
        if (app is not null)
            await CleanupAsync(() => app.DisposeAsync().AsTask(), TimeSpan.FromSeconds(30));
        if (errors.Count > 0)
            throw new AggregateException("Event Hubs cleanup failed", errors);

        async Task CleanupAsync(Func<Task> cleanup, TimeSpan timeout)
        {
            try { await cleanup().WaitAsync(timeout); }
            catch (Exception error) { errors.Add(error); }
        }
    }
}
