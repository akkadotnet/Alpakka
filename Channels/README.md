# Akka.Streams.Channels

An integration plugin between `Akka.Streams` and primitives from `System.Threading.Channels` Nuget package.

All features are encapsulated in two static classes:

1. `Akka.Streams.Channels.ChannelSource`:
	- `ChannelSource.FromReader<T>(ChannelReader<T> reader)` will connect itself as a consumer to a given `reader`.
2. `Akka.Streams.Channels.ChannelSink`:
	- `ChannelSink.FromWriter<T>(ChannelWriter<T> writer, bool isOwner)` will send elements directly to a given `writer`. If `isOwner` is set, it will also be responsible for completing `writer` once upstream completes.
	- `ChannelSink.AsReader<T>(int bufferSize, bool singleReader, BoundedChannelFullMode fullMode)` can be materialized into `ChannelReader<T>` used to consume events produced by akka stream.

Stages created this way will apply non-blocking backpressure rules to ensure resource-safe communication with channels.

### Example

This is an adapted client example from [official ASP.NET SignalR documentation](https://docs.microsoft.com/en-us/aspnet/core/signalr/streaming?view=aspnetcore-2.2#net-client):

```csharp
var materializer = actorSystem.Materializer();
var cancellationTokenSource = new CancellationTokenSource();
var channel = await hubConnection.StreamAsChannelAsync<int>(
    "Counter", 10, 500, cancellationTokenSource.Token);

await ChannelSource.FromReader(channel)
	.RunForeach(Console.WriteLine, materializer);

Console.WriteLine("Streaming completed");
```