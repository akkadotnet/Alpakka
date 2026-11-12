// //-----------------------------------------------------------------------
// // <copyright file="ServiceBusSpecBase.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Configuration;
using Azure.Messaging.ServiceBus;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.ServiceBus.Tests;

public class ServiceBusSpecBase: Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
{
    private IDistributedApplicationTestingBuilder? _builder;
    private DistributedApplication? _app;
    private ServiceBusClient? _client;
    private string? _connectionString;

    public ServiceBusSpecBase(Config config, string name, ITestOutputHelper output) : base(config, name, output)
    {
        Materializer = Sys.Materializer();
    }
    
    public ActorMaterializer Materializer { get; }
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("Application not initialized");

    public ServiceBusClient Client => _client ?? throw new InvalidOperationException("Container not initialized");

    public virtual async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _builder = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.ServiceBus_AppHost>(["--Testing=true"], startup.Token);
            _app = await _builder.BuildAsync(startup.Token);
            _builder = null;
            await _app.StartAsync(startup.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("messaging", startup.Token);
            _connectionString = await _app.GetConnectionStringAsync("messaging", startup.Token)
                ?? throw new InvalidOperationException("Service Bus connection string was not provided");
            _client = new ServiceBusClient(ConnectionString);
        }
        catch (Exception startupException)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Service Bus startup and cleanup both failed", startupException, cleanupException);
            }

            throw;
        }
    }

    public virtual async Task DisposeAsync()
    {
        var client = _client;
        var app = _app;
        var builder = _builder;
        _client = null;
        _app = null;
        _builder = null;
        _connectionString = null;

        List<Exception> errors = [];
        if (client is not null)
            await CleanupAsync(() => client.DisposeAsync().AsTask());
        if (builder is not null)
            await CleanupAsync(() => builder.DisposeAsync().AsTask());
        if (app is not null)
            await CleanupAsync(() => app.DisposeAsync().AsTask());
        if (errors.Count > 0)
            throw new AggregateException("Service Bus cleanup failed", errors);

        async Task CleanupAsync(Func<Task> cleanup)
        {
            try { await cleanup().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception error) { errors.Add(error); }
        }
    }
}
