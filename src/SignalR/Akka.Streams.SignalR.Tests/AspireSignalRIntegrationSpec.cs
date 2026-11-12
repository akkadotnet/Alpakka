#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Akka.Streams.SignalR.Tests;

public sealed class AspireSignalRFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private IDistributedApplicationTestingBuilder? _builder;
    private Uri? _endpoint;
    private readonly List<NativePeer> _peers = new();
    private readonly ConcurrentDictionary<string, byte> _heartbeats = new();

    public async Task InitializeAsync()
    {
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            _builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SignalR_AppHost>(cancellationToken: startup.Token);
            _app = await _builder.BuildAsync(startup.Token);
            _builder = null;
            await _app.StartAsync(startup.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("signalr", startup.Token);
            _endpoint = new Uri(_app.GetEndpoint("signalr", "http"), "echo");
        }
        catch (Exception startupError)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupError) { throw new AggregateException("SignalR startup and cleanup failed.", startupError, cleanupError); }
            throw;
        }
    }

    public async Task<NativePeer> ConnectAsync()
    {
        var connection = new HubConnectionBuilder().WithUrl(_endpoint ?? throw new InvalidOperationException("SignalR fixture is not ready."), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
        }).Build();
        var peer = new NativePeer(connection, this);
        _peers.Add(peer);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await connection.StartAsync(timeout.Token);
            Assert.False(string.IsNullOrWhiteSpace(connection.ConnectionId));
            // The source is hot. A real heartbeat establishes its subscription before business assertions.
            var excluded = _peers.Where(other => other != peer && other.Connection.State == HubConnectionState.Connected)
                .Select(other => other.ConnectionId).ToArray();
            while (true)
            {
                await connection.InvokeAsync("BroadcastExcept", peer.Heartbeat, excluded, timeout.Token);
                try
                {
                    await peer.Ready.WaitAsync(TimeSpan.FromMilliseconds(250), timeout.Token);
                    return peer;
                }
                catch (TimeoutException) { timeout.Token.ThrowIfCancellationRequested(); }
            }
        }
        catch (Exception startupError)
        {
            try { await peer.DisposeAsync(); }
            catch (Exception cleanupError) { throw new AggregateException("SignalR peer startup and cleanup failed.", startupError, cleanupError); }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (var peer in _peers.ToArray())
        {
            try { await peer.DisposeAsync(); }
            catch (Exception error) { failures.Add(error); }
        }
        var builder = _builder;
        _builder = null;
        if (builder is not null)
        {
            try { await builder.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception error) { failures.Add(error); }
        }
        var app = _app;
        _app = null;
        _endpoint = null;
        if (app is not null)
        {
            try { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0) throw new AggregateException("SignalR Aspire fixture cleanup failed.", failures);
    }

    public sealed class NativePeer : IAsyncDisposable
    {
        private readonly AspireSignalRFixture _owner;
        private readonly Channel<string> _received = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;
        internal string Heartbeat { get; } = "alpakka-warmup:" + Guid.NewGuid().ToString("N");
        internal Task Ready => _ready.Task;
        public HubConnection Connection { get; }
        public string ConnectionId => Connection.ConnectionId ?? throw new InvalidOperationException("The peer is not connected.");

        internal NativePeer(HubConnection connection, AspireSignalRFixture owner)
        {
            Connection = connection;
            _owner = owner;
            _owner._heartbeats.TryAdd(Heartbeat, 0);
            connection.On<string>(nameof(IClientSink.Receive), value =>
            {
                if (value == Heartbeat) _ready.TrySetResult(true);
                else if (!_owner._heartbeats.ContainsKey(value)) _received.Writer.TryWrite(value);
            });
        }

        public async Task ExpectAsync(string expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Equal(expected, await _received.Reader.ReadAsync(timeout.Token));
        }

        public async Task ExpectNoMessageAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _received.Reader.ReadAsync(timeout.Token).AsTask());
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            var failures = new List<Exception>();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Connection.StopAsync(timeout.Token);
            }
            catch (Exception error) { failures.Add(error); }
            try { await Connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception error) { failures.Add(error); }
            _owner._peers.Remove(this);
            if (failures.Count > 0) throw new AggregateException("Native SignalR peer cleanup failed.", failures);
        }
    }
}

public sealed class AspireSignalRIntegrationSpec(AspireSignalRFixture fixture) : IClassFixture<AspireSignalRFixture>
{
    [Theory]
    [InlineData("connection")]
    [InlineData("group")]
    [InlineData("broadcast")]
    public async Task Native_websockets_receive_exact_payloads_and_honor_routing_and_exclusions(string target)
    {
        await using var first = await fixture.ConnectAsync();
        await using var second = await fixture.ConnectAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var payload = "signalr-business:" + target + ":café:" + Guid.NewGuid().ToString("N");
        if (target == "connection")
        {
            await first.Connection.InvokeAsync("SendToConnection", second.ConnectionId, payload, deadline.Token);
            await second.ExpectAsync(payload);
            await first.ExpectNoMessageAsync();
        }
        else if (target == "group")
        {
            await second.Connection.InvokeAsync("JoinGroup", "workers", deadline.Token);
            await first.Connection.InvokeAsync("SendToGroup", "workers", payload, Array.Empty<string>(), deadline.Token);
            await second.ExpectAsync(payload);
            await first.ExpectNoMessageAsync();
            await first.Connection.InvokeAsync("JoinGroup", "workers", deadline.Token);
            await first.Connection.InvokeAsync("SendToGroup", "workers", payload + ":excluded", new[] { second.ConnectionId }, deadline.Token);
            await first.ExpectAsync(payload + ":excluded");
            await second.ExpectNoMessageAsync();
        }
        else
        {
            await first.Connection.InvokeAsync(nameof(IServerSource.Send), payload, deadline.Token);
            await first.ExpectAsync(payload);
            await second.ExpectAsync(payload);
            await first.Connection.InvokeAsync("BroadcastExcept", payload + ":excluded", new[] { first.ConnectionId }, deadline.Token);
            await second.ExpectAsync(payload + ":excluded");
            await first.ExpectNoMessageAsync();
        }
    }

    [Fact]
    public async Task Closing_and_replacing_a_native_client_keeps_the_shared_stream_usable()
    {
        await using var first = await fixture.ConnectAsync();
        await using var second = await fixture.ConnectAsync();
        await first.DisposeAsync();
        Assert.Equal(HubConnectionState.Disconnected, first.Connection.State);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var payload = "signalr-business:after-disconnect:" + Guid.NewGuid().ToString("N");
        await second.Connection.InvokeAsync(nameof(IServerSource.Send), payload, deadline.Token);
        await second.ExpectAsync(payload);
        await using var replacement = await fixture.ConnectAsync();
        await replacement.Connection.InvokeAsync(nameof(IServerSource.Send), payload + ":reconnected", deadline.Token);
        await second.ExpectAsync(payload + ":reconnected");
        await replacement.ExpectAsync(payload + ":reconnected");
    }
}
