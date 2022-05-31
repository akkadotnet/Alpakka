# Akka.Streams.Azure.EventHub

> __WARNING__
> 
> As of February 2020, the `Microsoft.Azure.EventHubs` package is deprecated in favor of the new `Azure.Messaging.EventHubs` package. Because of this, this package is also being deprecated in favor of the new `Azure.Messaging.EventHubs` SDK package.
> 
> We will release an interim NuGet package named `Akka.Streams.Azure.EventHub.V5` that uses the same `Akka.Streams.Azure.EventHub` namespace, this is done to preserve our package namespace in the future for easy migration. The new `Akka.Streams.Azure.EventHub.V5` package will replace the `Akka.Streams.Azure.EventHub` package on October 2022. Please migrate your package to the new `Akka.Streams.Azure.EventHub.V5` package.

Akka.Streams.Azure.EventHub is a streams connector for Azure EventHubs.

Library is based on [Microsoft.Azure.EventHubs](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/eventhub/Microsoft.Azure.EventHubs) driver, and implements Source and Sink to handle EventHubs message streams.

All stages are build with Akka.Streams advantages in mind:
- There is no constant EventHubs topics pooling: messages are consumed on demand, and with back-pressure support
- There is no internal buffering: consumed messages are passed to the downstream in realtime, and producer stages publish messages to EventHubs as soon as get them from upstream
- Each stage make use of it's own `EventHubClient` or `IEventProcessor` instance.
- All EventHubs failures can be handled with usual stream error handling strategies

## EventHub Client

An `EventHubClient` publishes messages to a single EventHubs name/topic. There are no guarantee that a single client can be shared between multiple sink stages without performance degradation as it does not provide message processing parallelism. 

User will need to instantiate an `EventHubClient` instance to be used for each sink stage. To create an `EventHubClient`, please read the corresponding documentation in Azure.

### EventHubSink

`EventHubSink` is used to publish messages into an EventHub name/topic. The sink consumes `IEnumerable<EventData>` elements that are sent in batches to EventHub. 

```C#
var client = EventHubClient.CreateFromConnectionString("{EventHubConnectionString}");
Source
    .From(Enumerable.Range(1, 100))
    .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
    .Grouped(10)
    .RunWith(new EventHubSink(client), materializer);
```

Or you can use a shortcut convenience method

```C#
var client = EventHubClient.CreateFromConnectionString("{EventHubConnectionString}");
Source
    .From(Enumerable.Range(1, 100))
    .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
    .Grouped(10)
    .ToEventHub(client, materializer);
```

The materialized value of the sink is a `Task` which is completed with result when the stream completes or with exception if an error occurs.

## EventHubSource

`EventHubSource` is used to consume messages from an EventHub name/topic. The source emits `Tuple<PartitionContext, EventData>` to be consumed by the stream. The materialized value of the source is an `IEventProcessor` that are used to process incoming events from the EventHub name/topic. 

`EventHubSource` can not be used directly, it only contains the processing part of an EventHub processing chain; it needs to be bootstrapped to a `EventProcessorHost` that in turn  are responsible for connecting and retrieving data from an EventHub name/topic.

### SingleProcessorFactory

`SingleProcessorFactory` is an `IEventProcessorFactory` implementation that is responsible to return a single `IEventProcessor` instance. It is used to bootstrap a single materialized `EventHubSource` with an `EventProcessorHost`. In this scenario, a single materialized `EventHubSource` is being used to process events from all partitions in the EventHub name/topic.

```C#
var processor = Source.FromGraph(new EventHubSource())
    .Select(t =>
    {
        var (partitionContext, eventData) = t;
        Console.WriteLine("Message from Partition: " + partitionContext.Lease.PartitionId);
        return eventData;
    })
    .Select(e => Encoding.UTF8.GetString(e.Body))
    .ToMaterialized(Sink.ForEach((string s) => Console.WriteLine(s)), Keep.Left)
    .Run(mat);
       
var eventProcessorHost = new EventProcessorHost(
    "{EventProcessorHostName}",
    "{EventHubName}",
    "{EventHubConsumerGroup}",
    "{EventHubConnectionString}",
    "{StorageConnectionString}");
eventProcessorHost.RegisterEventProcessorFactoryAsync(new SingleProcessorFactory(processor));
```

### MultiProcessorFactory

`MultiProcessorFactory` is an `IEventProcessorFactory` implementation that is responsible to materialize a new `EventHubsSource` and return a new `IEventProcessor` every time its `CreateEventProcessor()` is invoked. It is used to bootstrap an `EventHubSource` graph with an `EventProcessorHost`. In this scenario, a new `EventHubSource` is materialized to process events for each partition in the EventHub name/topic.

```C#
var runnableGraph = Source.FromGraph(new EventHubSource())
    .Select(t =>
    {
        var (partitionContext, eventData) = t;
        Console.WriteLine("Message from Partition: " + partitionContext.Lease.PartitionId);
        return eventData;
    })
    .Select(e => Encoding.UTF8.GetString(e.Body))
    .ToMaterialized(Sink.ForEach((string s) => Console.WriteLine(s)), Keep.Left);

var eventProcessorHost = new EventProcessorHost(
    "{EventProcessorHostName}",
    "{EventHubName}",
    "{EventHubConsumerGroup}",
    "{EventHubConnectionString}",
    "{StorageConnectionString}");
eventProcessorHost.RegisterEventProcessorFactoryAsync(new MultiProcessorFactory(runnableGraph, mat));
```

Note in the code example above that the source is not materialized, we're passing in a graph into the factory. The factor will then materialize the graph for every partition in the EventHub name/topic.

### Event Processor Host

An `EventProcessorHost` subscribes to an EventHub name/topic and passes the messages into an Akka Stream, it is responsible for instantiating any `IEventProcessor` registered through a registered `IEventProcessorFactory` for each partition inside the EventHub.

## Error handling
Akka.Streams.Azure.EventHub stages utilizes stream supervision deciders to dictate what happens when a failure or exception is thrown from inside the stream stage. These deciders are basically delegate functions that returns an `Akka.Streams.Supervision.Directive` enumeration to tell the stage how to behave when a specific exception occured during the stream lifetime.

You can read more about stream supervision strategies in the [Akka documentation](https://getakka.net/articles/streams/error-handling.html#supervision-strategies)

> __NOTE:__
>
> A decider applied to a stream using
> `.WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider))` will be used for the whole
> stream, any exception that happened in any of the stream stages will use the same decider
> to determine their fault behavior.