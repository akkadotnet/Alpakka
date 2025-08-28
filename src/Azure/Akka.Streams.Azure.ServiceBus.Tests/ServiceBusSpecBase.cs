// //-----------------------------------------------------------------------
// // <copyright file="ServiceBusSpecBase.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Configuration;
using Azure.Messaging.ServiceBus;
using Testcontainers.ServiceBus;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.ServiceBus.Tests;

public class ServiceBusSpecBase: Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
{
    private ServiceBusContainer? _container;
    private ServiceBusClient? _client;

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
        _container = new ServiceBusBuilder()
            .WithConfig("./config/config.json")
            .WithAcceptLicenseAgreement(true)
            .Build();
        await _container.StartAsync();
        
        _client = new ServiceBusClient(ConnectionString);
    }

    public virtual async Task DisposeAsync()
    {
        if (_client != null)
            await _client.DisposeAsync();
        
        if(_container is not null)
            await _container.DisposeAsync();
    }
}