# Akka.Streams.SignalR

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.SignalR.svg)](https://www.nuget.org/packages/Akka.Streams.SignalR/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.SignalR.svg)](https://www.nuget.org/packages/Akka.Streams.SignalR/)

ASP.NET Core SignalR connections as Akka.NET Streams sources and sinks. The NuGet package targets .NET 6; the examples below use .NET 10 applications. This connector uses the ASP.NET Core SignalR hub protocol, with client-to-server method `Send` and server-to-client method `Receive`.

```sh
dotnet add package Akka.Streams.SignalR
```

## Host an echo hub

Use a Web SDK project. Register `AddSignalRAkkaStream`, ASP.NET Core SignalR and the dependencies of your `StreamConnector`. Derive a hub from `StreamHub<TConnector>`; hub instances are created by ASP.NET Core for individual operations, while the default dispatcher retains a connector for its stream type.

This complete server echoes received values to their originating connection. The explicit settings choose a buffer of 128 and `DropHead` for this application.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR;
using Akka.Streams.SignalR.Internals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
using var system = ActorSystem.Create("signalr-echo");
builder.Services.AddSingleton(system);
builder.Services.AddSingleton(new ConnectionSourceSettings(128, OverflowStrategy.DropHead));
builder.Services.AddSignalRAkkaStream().AddSignalR();
await using var app = builder.Build();
app.MapHub<EchoHub>("/echo");
try
{
    await app.StartAsync();
    Console.WriteLine("SIGNALR_READY " + app.Urls.First() + "/echo");
    await app.WaitForShutdownAsync();
}
finally { await system.Terminate(); }

public sealed class EchoHub(IStreamDispatcher dispatcher) : StreamHub<EchoStream>(dispatcher);

public sealed class EchoStream : StreamConnector
{
    public EchoStream(IHubClients clients, ActorSystem system, ConnectionSourceSettings sourceSettings,
        ConnectionSinkSettings sinkSettings = null) : base(clients, sourceSettings, sinkSettings)
    {
        Source.Where(value => value is Received)
            .Select(value => (Received)value)
            .Select(value => Signals.Send(value.Request.ConnectionId, value.Data))
            .To(Sink).Run(system.Materializer());
    }
}
```

Run the server with `dotnet run --urls http://localhost:5000`. Its printed `SIGNALR_READY` address identifies the endpoint for a client.

## Connect a client

In a separate console application, install a `Microsoft.AspNetCore.SignalR.Client` version compatible with that application's target framework, alongside `Akka.Streams.SignalR`. Set `SIGNALR_URL` to the printed server endpoint, or use the default below.

This complete client explicitly selects WebSockets. It establishes readiness with a bounded heartbeat before sending the application payload. Heartbeats are application setup traffic; the payload assertion is not retried.

```csharp
using Akka.Streams.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

var url = Environment.GetEnvironmentVariable("SIGNALR_URL") ?? "http://localhost:5000/echo";
await using var connection = new HubConnectionBuilder().WithUrl(url, options =>
{
    options.Transports = HttpTransportType.WebSockets;
}).Build();
var heartbeat = "ready:" + Guid.NewGuid().ToString("N");
var payload = "Hello café";
var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
connection.On<string>(nameof(IClientSink.Receive), value =>
{
    if (value == heartbeat) ready.TrySetResult(true);
    else received.TrySetResult(value);
});
using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
await connection.StartAsync(startup.Token);
while (!ready.Task.IsCompleted)
{
    await connection.InvokeAsync(nameof(IServerSource.Send), heartbeat, startup.Token);
    try { await ready.Task.WaitAsync(TimeSpan.FromMilliseconds(250), startup.Token); }
    catch (TimeoutException) { startup.Token.ThrowIfCancellationRequested(); }
}
await connection.InvokeAsync(nameof(IServerSource.Send), payload, startup.Token);
var reply = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
if (reply != payload) throw new InvalidOperationException("Unexpected echo payload.");
Console.WriteLine(reply);
await connection.StopAsync();
```

## Events, routing and completion

`StreamConnector.Source` emits `Connected`, `Received` and `Disconnected`, each carrying the original `HubCallerContext` in `Request`. `Received.Data` is an object decoded by the configured SignalR protocol; it is not a typed domain-message guarantee. `Disconnected.Exception` is the disconnect error; `StopCalled` reports that the exception is null.

The source is hot. Each materialization subscribes independently and has its own buffer. Events before a source stage subscribes are not replayed, including an initial connection event racing connector creation. Materialize processing graphs before relying on incoming traffic; an application-level handshake can establish readiness. Client `Send` invocation completion acknowledges hub dispatch, not downstream processing or delivery.

`StreamConnector.Sink` accepts:

- `Signals.Send(connectionId, data)` for one connection.
- `Signals.SendToGroup(group, data, excluded)` for a group with optional excluded connection IDs.
- `Signals.Broadcast(data, excluded)` for all clients except the supplied IDs.

Group membership is managed by ASP.NET Core, for example through a hub's `Groups.AddToGroupAsync`. The sink sends `Receive(data)` sequentially, awaiting each transport task before pulling the next item. It does not block the interpreter while a send is pending. Faulted transport tasks preserve their `AggregateException` wrapper; canceled transport tasks fail with `TaskCanceledException`.

Both source and sink materialize **NotUsed**. The sink does not expose a final delivery task; application acknowledgments are needed when peer receipt matters. Upstream failure can terminate a pending stream, but already issued transport calls are not canceled automatically and may still have external effects. Stream cancellation detaches that source's event subscription; the ASP.NET Core host owns its hub clients/connections. A single client disconnect does not shut down the shared connector stream.

## Buffering and current limits

SignalR cannot apply stream backpressure to incoming client events. `ConnectionSourceSettings` supports `DropHead`, `DropTail`, `DropNew`, `DropBuffer` and `Fail`; `Backpressure` is rejected. `Fail` uses `BufferOverflowException` when the local queue overflows. Configure a positive buffer size explicitly for your workload.

`ConnectionSourceSettings.Default` is 128 with `DropHead`. The default dispatcher's fallback when no settings service is registered is separately 100 with `DropTail`. Registering settings as in the example makes the intended behavior explicit. `WithBufferSize` and `WithOverflowStrategy` return copied settings. `ConnectionSourceSettings.Create(config)` reads `buffer-capacity`/`overflow-strategy`; `Create(system)` reads `akka.streams.signalr.source` from that system's configuration.

The default dispatcher caches by stream connector type. Use distinct connector types for different hubs; its cache does not distinguish hub types using the same connector. Concurrent first access can also run the construction factory more than once. Authentication, authorization, durable delivery, replay, cross-server groups/backplanes and exactly-once processing are application/deployment concerns, not established by these tests.

The package retains standalone ASP.NET Core dependencies so existing plain SDK consumers can still restore it: SignalR 1.2.14, HTTP Connections 1.2.14 and WebUtilities 2.3.14 are the minimum selected patches. Applications using the ASP.NET Core shared framework must patch their installed runtime separately. The retained .NET 6 library target preserves compatibility; .NET 6 is out of support, so use a currently supported application runtime.

## Tests and sample

From the repository root:

```sh
dotnet test src/SignalR/Akka.Streams.SignalR.Tests/Akka.Streams.SignalR.Tests.csproj -c Release --framework net10.0
aspire start --isolated --apphost src/SignalR/Examples/SignalR.AppHost/SignalR.AppHost.csproj
```

The repaired sample AppHost uses Aspire.Hosting.Testing-compatible 13.6.5 hosting, a discovered endpoint and a loopback-only local dashboard profile. It requires .NET 10 and the Aspire orchestration tools, with no Docker or cloud account. Its EchoHub also demonstrates group membership, direct sends and exclusions.

The suite retains four TestServer cases with an explicit subscription barrier and WebSocket transport, adds twelve controlled sink contracts, and runs four Aspire-owned Kestrel/native WebSocket cases. Native peers assert exact UTF-8 payloads, recipient/exclusion behavior and successful communication after client replacement. Readiness heartbeats are separated from business-message assertions. Clients, builders and the application have bounded owned cleanup, including startup failure paths. The tests do not establish deployment-scale behavior or universal disconnect/cold-start guarantees.
