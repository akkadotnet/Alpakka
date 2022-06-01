# Akka.Streams.Azure.EventHub.V5

> __WARNING__
>
> As of February 2020, the `Microsoft.Azure.EventHubs` package is deprecated in favor of the new `Azure.Messaging.EventHubs` package. Because of this, this package will be an interim NuGet package implementing the new `Azure.Messaging.EventHubs` that uses the same `Akka.Streams.Azure.EventHub` namespace, this is done to preserve our package namespace in the future for easy migration. 
> 
> This `Akka.Streams.Azure.EventHub.V5` package will replace the `Akka.Streams.Azure.EventHub` package on October 2022. If you're still using `Akka.Streams.Azure.EventHub`, please migrate to the new `Akka.Streams.Azure.EventHub.V5` package.

Akka.Streams.Azure.EventHub.V5 is a streams connector for Azure EventHubs.

Library is based on [Azure.Messaging.EventHubs](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/eventhub/Microsoft.Azure.EventHubs) driver, and implements Source and Sink to handle EventHubs message streams.

All stages are build with Akka.Streams advantages in mind:
- There is no constant EventHubs topics pooling: messages are consumed on demand, and with back-pressure support
- There is no internal buffering: consumed messages are passed to the downstream in realtime, and producer stages publish messages to EventHubs as soon as get them from upstream
- Each stage make use of it's own `EventHubProducerClient` or `EventProcessorClient` instance.
- All EventHubs failures can be handled with usual stream error handling strategies

## EventHub Producer Client

An `EventHubProducerClient` publishes messages to a single EventHubs name/topic.  You will need to instantiate an `EventHubProducerClient` instance to be used for each sink stage.

To create an `EventHubProducerClient`, please read the corresponding Azure EventHub documentation.

### EventHubSink

`EventHubSink` is used to publish messages into an EventHub name/topic. The sink consumes `IEnumerable<EventData>` elements that are sent in batches to EventHub.

```C#
var client = new EventHubProducerClient(
    "{EventHubConnectionString}", 
    "{EventHubName}");

var task = Source.From(Enumerable.Range(1, 100))
    .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
    .Grouped(10)
    .RunWith(new EventHubSink(client), materializer);
```

Or you can use a shortcut convenience method

```C#
var client = new EventHubProducerClient(
    "{EventHubConnectionString}", 
    "{EventHubName}");

var task = Source.From(Enumerable.Range(1, 100))
    .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
    .Grouped(10)
    .ToEventHub(client, materializer);
```

The materialized value of the sink is a `Task` which is completed with result when the stream completes or with exception if an error occurs.

## EventHub Sources

### EventHubSource
`EventHubSource` is used to consume messages from an EventHub name/topic. The source emits `ProcessContext` to be consumed by the stream. The materialized value of the source is an `EventProcessorClient` created by the processor factory that can be used to stop the client from processing further events from EventHub.

`ProcessContext` encapsulates the `PartitionContext` and `DataEvent` of the currently processed event. It also contains an `UpdateCheckpointAsync()` method that can be used to update the current EventHub checkpoint to the current event offset.

> __NOTE__
> 
> The `DataEvent` property can be null when 'EventProcessorClient' emit an empty event. Trying to call `UpdateCheckpointAsync()` on an empty event will throw an exception.

#### EventProcessorClient Factory

`IProcessorFactory` is a simple interface that contains a single `CreateProcessor()` method that needs to be implemented by the user. Inside this method, the user is responsible of creating a new instance of `EventProcessorClient` that can be used by the source to receive events from EventHub. A single `IProcessorFactory` can be used to configure multiple `EventHubSource` graphs.

`EventProcessorClient` is the default EventHub processor that came built-in inside `Azure.Messaging.EventHubs.Processor` package. Please refer to Azure EventHub documentation on how to create a `EventProcessorClient` specific to your network configuration needs.

```c#
public class EventProcessorFactory : IProcessorFactory<EventProcessorClient>
{
    public EventProcessorClient CreateProcessor()
    {
        var storageClient  = new BlobContainerClient(
            "{StorageConnectionString}", 
            "{BlobContainerName}");
        return new EventProcessorClient(
            storageClient, 
            "{ConsumerGroupName}",
            "{EventHubConnectionString}",
            "{EventHubName}");
    }
}
```

#### Creating the EventHubSource

To create the `EventHubSource`, you will need to pass in the `IProcessorFactory` implementation instance.

```c#
var processor = Source.FromGraph(new EventHubSource(new EventProcessorFactory()))
    .SelectAsync(1, async t =>
    {
        Console.WriteLine($"Message from Partition: {t.Partition.PartitionId}");
        await Business(t);
        return Done.Instance;
    })
    .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
    .Run(mat);
```

You can stop the processor from consuming more events from EventHub by calling `processor.StopProcessingAsync()`. 

> __NOTE__
> 
> There are instances where `EventProcessorClient` deadlocks itself when `StopProcessingAsync()` are invoked. Make sure that you guard the await invocation to make sure you don't deadlock your own application.

## BatchedEventHubSource

`BatchedEventHubSource` is a specialized stream source that is used to consume messages from an EventHub name/topic and emits batches of messages. The source emits `BatchProcessContext` to be consumed by the stream. The materialized value of the source is a custom `BatchedEventProcessorClient` created by the processor factory that can be used to stop the client from processing further events from EventHub.

`BatchProcessContext` encapsulates the `PartitionContext` and `List<DataEvent>` of the currently processed event batch. It also contains an `UpdateCheckpointAsync()` method that can be used to update the current EventHub checkpoint to the highest event offset inside the batch.

> __NOTE__
>
> The `List<DataEvent>` property can be null when `BatchedEventProcessorClient` emit an empty event. Trying to call `UpdateCheckpointAsync()` on an empty event will throw an exception.

#### BatchedEventProcessorClient Factory

Just like using `EventProcessorClient`, you need to implement `IProcessorFactory.CreateProcessor()` that returns an instance of `BatchedEventProcessorClient`. A single processor factory can be used to configure multiple `BatchedEventHubSource` graphs.

`BatchedEventProcessorClient` is a custom EventHub processor that emits events in batches; it has the same exact constructors and can be configured the same way as `EventProcessorClient`. Please refer to Azure EventHub documentation on `EventProcessorClient` creation as it also applies to `BatchedEventProcessorClient`.

```c#
public class BatchedEventProcessorFactory 
    : IProcessorFactory<BatchedEventProcessorClient>
{
    public BatchedEventProcessorClient CreateProcessor()
    {
        var storageClient  = new BlobContainerClient(
            "{StorageConnectionString}", 
            "{BlobContainerName}");
        return new BatchedEventProcessorClient(
            storageClient, 
            "{ConsumerGroupName}",
            "{EventHubConnectionString}",
            "{EventHubName}");
    }
}
```

#### Creating the BatchedEventHubSource

To create the `BatchedEventHubSource`, you will need to pass in the `IProcessorFactory` implementation instance.

```c#
var processor = Source.FromGraph(new EventHubSource(new BatchedEventProcessorFactory()))
    .SelectAsync(1, async t =>
    {
        Console.WriteLine($"Message from Partition: {t.Partition.PartitionId}");
        await Business(t);
        return Done.Instance;
    })
    .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
    .Run(mat);
```

You can stop the processor from consuming more events from EventHub by calling `processor.StopProcessingAsync()`.

> __NOTE__
>
> There are instances where `EventProcessorClient` deadlocks itself when `StopProcessingAsync()` are invoked. Make sure that you guard the await invocation to make sure you don't deadlock your own application.

## Error handling
Akka.Streams.Azure.EventHub.V5 stages utilizes stream supervision deciders to dictate what happens when a failure or exception is thrown from inside the stream stage. These deciders are basically delegate functions that returns an `Akka.Streams.Supervision.Directive` enumeration to tell the stage how to behave when a specific exception occured during the stream lifetime.

You can read more about stream supervision strategies in the [Akka documentation](https://getakka.net/articles/streams/error-handling.html#supervision-strategies)

> __NOTE:__
>
> A decider applied to a stream using `.WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider))` will be used for the whole stream, any exception that happened in any of the stream stages will use the same decider to determine their fault behavior.