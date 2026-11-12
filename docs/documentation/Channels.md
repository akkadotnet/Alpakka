# System.Threading.Channels in Akka.Streams

The separate [Akka.Streams.Channels](https://www.nuget.org/packages/Akka.Streams.Channels/1.5.37) package is already deprecated on NuGet. The channel adapters are in Akka.Streams; new code does not need an Alpakka Channels package.

## Source migration

These public factories are present in the inspected Akka.Streams 1.5.60 baseline:

| Previous call | Core call | Materialized value |
| --- | --- | --- |
| `Akka.Streams.Channels.ChannelSource.FromReader<T>(reader)` | `Source.ChannelReader<T>(reader)` | `NotUsed` |
| `Akka.Streams.Channels.ChannelSink.FromWriter<T>(writer, isOwner)` | `Sink.FromWriter<T>(writer, isOwner)` | `NotUsed` |
| `Akka.Streams.Channels.ChannelSink.AsReader<T>(bufferSize, singleReader, fullMode)` | `Sink.ChannelReader<T>(bufferSize, singleReader, fullMode)` | `ChannelReader<T>` |

Replace the old namespace imports and rebuild before removing the old package. Pass `singleReader` explicitly to `Sink.ChannelReader`; the legacy helper defaulted it to `false`. Preserve `isOwner`: `false` leaves completion of the supplied writer to the caller, while `true` gives the sink responsibility for completion/failure. Do not assume changing a channel's full-buffer mode preserves delivery semantics; dropping modes intentionally allow loss.

This complete example reads an application-owned channel. The application completes its writer after sending both values:

```csharp
using System.Threading.Channels;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;

var channel = Channel.CreateBounded<string>(1);
var system = ActorSystem.Create("channel-reader-example");
try
{
    var received = Source.ChannelReader(channel.Reader)
        .RunWith(Sink.Seq<string>(), system.Materializer());
    await channel.Writer.WriteAsync("first café");
    await channel.Writer.WriteAsync("second λ");
    channel.Writer.Complete();
    var values = await received.WaitAsync(TimeSpan.FromSeconds(5));
    if (!values.SequenceEqual(new[] { "first café", "second λ" }))
        throw new InvalidOperationException("Channel payloads changed.");
    Console.WriteLine(string.Join(" | ", values));
}
finally
{
    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
}
```

The example was compiled and executed against Akka.Streams 1.5.60. Source delivery and caller-owned/owned writer cases were also compared with the old package.

## Bounded sink completion

There is a separate upstream defect in older writer-backed channel sinks: completion while a bounded write is pending can discard the final item. A capacity-one/two-item comparison reproduced it in the old Channels package and Akka.Streams 1.5.60; the same core case passed with 1.5.74. See [Akka.NET issue #8285](https://github.com/akkadotnet/akka.net/issues/8285) and [the upstream fix #8289](https://github.com/akkadotnet/akka.net/pull/8289).

Test bounded writes and completion with the actual core version used by your application. The factory mapping alone is not a claim that an older core version fixes this defect. Dependency upgrades require their own compatibility review; this migration guide does not change Alpakka's Akka minimum.
