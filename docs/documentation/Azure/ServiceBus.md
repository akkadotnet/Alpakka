# Azure ServiceBus adapter


## ServiceBusSource

You can create a `Source` for the ServiceBus via one of the `ServiceBusSource.Create` factory methods. 
 - `Create<T>(client)` extracts the message via `brokered.GetBody<T>()` and completes the original `BrokeredMessage` before emits downstream
 - `Create(client)` emit's the raw `BrokeredMessage`'s downstream
 - `Create<T>(client, msg => {...}` can be used for custom extraction and or completion

> Note: You're responsible for marking processed messages as completed if you're not using the first factory.

By default the `Source` will complete the stream with failure if a call to the ServiceBus for new messages failed, you can change that behavior by using `Restart` or `Resume` `SupervisionStrategy`.

The `Source` reads messages in batches from the ServiceBus and then emits the single messages into the stream, once all messages are emitted another request is send to the ServiceBus. The number of messages that are requested per batch can be configured via the `maxMessageCount` parameter, by default 100 messages.
If the ServiceBus is empty the source will periodically poll for new messages, this interval can be configured via the `pollInterval` parameter, by default 10 seconds.
You can furthermore configure the time span that the server will wait for the message batch to arrive before it times out by setting the `serverWaitTime` parameter, default is 3 seconds.

The `Source` supports the `QueueClient` as well as the `SubscriptionClient`.

## ServiceBusSink

You can create a `Sink` for the ServiceBus either via `Sink.FromGraph(new ServiceBusSink)` or by calling the `ServiceBusSink.Create` method or use the extension method `ToServiceBus` on a `Source<IEnumerable<BrokeredMessage>, TMat>` directly.
The `Sink` is materialized into a `Task` which will be completed with `Success` when reaching the normal end of the stream, or completed with `Failure` if there is a failure signaled in the stream.

You can configure different behaviors if a batch couldn't be send to the ServiceBus by using the `SupervisionStrategy` attribute, the following behaviors are available: 

- `Stop`: Default behavior, completes the stream with failure. 
- `Resume`: Sends the batch again. 
- `Restart`: Skips the current batch and continues with the next batch.  

> Note: You need to make sure that the batch isn't exceeding the size limit of one event data transmission, which is 256k by default.

The `Sink` supports the `QueueClient` as well as the `TopicClient`.

## Examples

A example can be found in the [examples project](https://github.com/akkadotnet/Alpakka/tree/dev/src/Azure/Examples/Akka.Streams.Azure.ServiceBus.Examples).
