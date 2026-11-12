// //-----------------------------------------------------------------------
// // <copyright file="ServiceBusSpecBase.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Configuration;
using Azure.Messaging.ServiceBus;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.ServiceBus.Tests;

public class ServiceBusSpecBase: Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
{
    private ServiceBusContainer? _container;
    private ServiceBusClient? _client;
    private MsSqlContainer? _database;
    private INetwork? _network;

    public ServiceBusSpecBase(Config config, string name, ITestOutputHelper output) : base(config, name, output)
    {
        Materializer = Sys.Materializer();
    }
    
    public ServiceBusContainer Container => _container ?? throw new InvalidOperationException("Container not initialized");
    public ActorMaterializer Materializer { get; }
    public string ConnectionString => _container?.GetConnectionString() ?? throw new InvalidOperationException("Container not initialized");

    public ServiceBusClient Client => _client ?? throw new InvalidOperationException("Container not initialized");

    public virtual async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _network = new NetworkBuilder().Build();
            // Pin the companion used by the reviewed Aspire fixture: the module's default CU14 tag is unavailable.
            _database = new MsSqlBuilder("mcr.microsoft.com/mssql/server@sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718")
                .WithNetwork(_network)
                .WithNetworkAliases("servicebus-database")
                .Build();
            _container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.0")
                .WithMsSqlContainer(_network, _database, "servicebus-database")
                .WithConfig("./config/config.json")
                .WithAcceptLicenseAgreement(true)
                .Build();
            await _container.StartAsync(startup.Token);
            _client = new ServiceBusClient(ConnectionString);
        }
        catch (Exception startupException)
        {
            try { await DisposeAsync(); }
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
        var container = _container;
        var database = _database;
        var network = _network;
        _client = null;
        _container = null;
        _database = null;
        _network = null;

        List<Exception> errors = [];
        if (client is not null)
            await CleanupAsync(() => client.DisposeAsync().AsTask());
        if (container is not null)
            await CleanupAsync(() => container.DisposeAsync().AsTask());
        if (database is not null)
            await CleanupAsync(() => database.DisposeAsync().AsTask());
        if (network is not null)
            await CleanupAsync(() => network.DisposeAsync().AsTask());
        if (errors.Count > 0)
            throw new AggregateException("Service Bus cleanup failed", errors);

        async Task CleanupAsync(Func<Task> cleanup)
        {
            try { await cleanup().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception error) { errors.Add(error); }
        }
    }
}
