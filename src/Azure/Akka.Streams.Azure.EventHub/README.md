# Akka.Streams.Azure.EventHub

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Azure.EventHub.svg)](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Azure.EventHub.svg)](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub/)

Azure Event Hubs sources and a sink for Akka.NET Streams, using `Azure.Messaging.EventHubs.Processor`. The package targets .NET Standard 2.0 and .NET 6. It is the current `Akka.Streams.Azure.EventHub` package; older instructions to install the interim `.V5` package no longer apply.

```sh
dotnet add package Akka.Streams.Azure.EventHub
```

You need an Event Hubs namespace, an event hub, send/receive credentials and a consumer group. Sources also need an existing Azure Blob container for partition ownership and checkpoints. Provision these separately. See the [Azure SDK processor documentation](https://learn.microsoft.com/dotnet/api/overview/azure/messaging.eventhubs.processor-readme) for authentication and processor configuration.

## Publish grouped events

`EventHubSink.Create(producer)` accepts `IEnumerable<EventData>` elements and materializes a `Task`. It sends each group in SDK batches and completes the task after upstream completion and the final send. Its `EventHubProducerClient` is borrowed: the caller must keep it alive until the task finishes and dispose it afterwards.

This complete example reads an Event Hubs connection string from `EVENTHUB_CONNECTION_STRING`. The hub name is `orders`.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.EventHub;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

var system = ActorSystem.Create("eventhub-publisher");
try
{
    await using var producer = new EventHubProducerClient(
        Environment.GetEnvironmentVariable("EVENTHUB_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set EVENTHUB_CONNECTION_STRING."), "orders");
    await Source.From(new[] { "first", "second", "third" })
        .Select(value => new EventData(value))
        .Grouped(10)
        .RunWith(EventHubSink.Create(producer), system.Materializer());
}
finally { await system.Terminate(); }
```

The sink uses the producer's default partition routing. It does not offer a partition-selection option or an exactly-once delivery guarantee. Keep each event within the service's maximum event size. SDK batching and retries, stream buffers and the sink's pending-send list still apply.

## Consume and checkpoint

`EventHubSource.Create(factory)` emits `ProcessContext` and materializes the factory's `EventProcessorClient`. Return a **new processor on each factory call**; the stage installs the processor's event handlers and starts it. Do not share that processor between materializations or attach competing SDK handlers.

A context exposes `Data`, `HasEvent`, `Partition`, `CancellationToken` and `UpdateCheckpointAsync()`. Empty SDK receive callbacks can have `HasEvent == false` and `Data == null`; do not checkpoint those. Checkpoint explicitly **after successful application processing**. Emitting a context releases the SDK callback; it does not mean downstream processing or checkpointing has completed. The SDK prefetches, and the stage and stream can buffer contexts. Cross-partition ordering and exactly-once processing are not guaranteed; make effects safe to repeat.

This complete example runs for 30 seconds. Set `EVENTHUB_CONNECTION_STRING` and `BLOB_CONNECTION_STRING`; create an `orders-consumer` group and a `checkpoints` Blob container first. Its kill switch cancels the source; it does not drain in-flight application work. Shutdown can cancel a checkpoint, leaving that event to be repeated after restart.

```csharp
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.EventHub;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;

var system = ActorSystem.Create("eventhub-consumer");
EventProcessorClient? processor = null;
try
{
    var running = EventHubSource.Create(new ProcessorFactory())
        .ViaMaterialized(KillSwitches.Single<ProcessContext>(), Keep.Both)
        .SelectAsync(1, async context =>
        {
            if (context.HasEvent)
            {
                Console.WriteLine(context.Data!.EventBody.ToString());
                await context.UpdateCheckpointAsync(context.CancellationToken);
            }
            return Done.Instance;
        })
        .ToMaterialized(Sink.Ignore<Done>(), Keep.Both)
        .Run(system.Materializer());
    processor = running.Item1.Item1;
    await Task.Delay(TimeSpan.FromSeconds(30));
    running.Item1.Item2.Shutdown();
    try { await running.Item2; }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Shutdown canceled in-flight processing; uncheckpointed events can be replayed.");
    }
}
finally
{
    try
    {
        if (processor is not null)
            await processor.StopProcessingAsync();
    }
    finally { await system.Terminate(); }
}

sealed class ProcessorFactory : IProcessorFactory<EventProcessorClient>
{
    public EventProcessorClient CreateProcessor() => new(
        new BlobContainerClient(Required("BLOB_CONNECTION_STRING"), "checkpoints"),
        "orders-consumer", Required("EVENTHUB_CONNECTION_STRING"), "orders");

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Set {name}.");
}
```

The source starts from the SDK's default position when no checkpoint exists. Checkpoints control subsequent starts for the same namespace, hub, consumer group and storage container. A different group or container creates an independent checkpoint history.

`BatchedEventHubSource.Create(IProcessorFactory<BatchedEventProcessorClient>)` instead emits `BatchProcessContext`, with a `List<EventData>` in `Data`, and materializes the custom `BatchedEventProcessorClient`. Supply that processor through its own factory. Empty batches are not emitted. Process the whole batch successfully before calling its `UpdateCheckpointAsync()`: that checkpoints the highest event offset/sequence in the batch, so partially processed batches must not be checkpointed. Batch boundaries come from SDK receives; they are not a fixed application batch size.

Both sources hold a receive callback until downstream accepts its context. Cancellation releases waiting callbacks and initiates asynchronous processor shutdown, including on failure. **Stream termination can precede processor shutdown.** Await the materialized processor's `StopProcessingAsync()` before disposing checkpoint dependencies. Failures during automatic shutdown are logged; they cannot change an already terminated stream. Do not synchronously stop a processor from inside its own event callbacks.

By default, SDK processing errors fail the source and send errors fail the sink's materialized task. Existing stream supervision attributes can change this behavior: the source's Resume/Restart directives resume pulling rather than recreate its processor; the sink's Resume drops the failed send and Restart retries it. Retries may duplicate delivery. Errors in application processing follow the supervision behavior of that downstream stage.

## Local development and tests

The repository's Event Hubs sample AppHost starts the official Event Hubs emulator and Azurite. With Docker running:

```sh
aspire start --isolated --apphost src/Azure/Examples/Simple/EventHub/EventHub.AppHost/EventHub.AppHost.csproj
dotnet test src/Azure/Akka.Streams.Azure.EventHub.Tests/Akka.Streams.Azure.EventHub.Tests.csproj -c Release
```

The tests use `Aspire.Hosting.Testing` with the same AppHost in test mode, without sample producers/consumers, discovered connection strings and isolated containers. They cover native-SDK sink interoperability, Blob checkpoint persistence and processor recreation, plus deterministic backpressure, cancellation and failure contracts. Missing Docker or emulator startup failures fail the suite. Emulator tests do not establish Azure service identity/RBAC, production throughput, cloud partition distribution or persistence across an emulator restart.
