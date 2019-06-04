# Akka.Streams.SQS

This module is part of Akka.NET Alpakka project. It's a connector, which exposes interation with Amazon Simple Queue Service (SQS) using Akka.NET stream primitives.

## Demo

```csharp
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Akka.Actor;
using Akka.Streams;

var credentials = new BasicAWSCredentials(AccessKey, SecretKey);

using (var client = new AmazonSQSClient(credentials, RegionEndpoint.USEast1))
using (var system = ActorSystem.Create("system"))
using (var materializer = system.Materializer())
{
    var settings = SqsSourceSettings.Default
        .WithVisibilityTimeout(TimeSpan.FromSeconds(60));

    await SqsSource.Create(client, QueueUrl, settings)
        .Select(msg =>
        {
            // handle message
            Console.WriteLine($"Received message from SQS: {msg.Body}");
            // after we handled the message we need to take if off from the queue
            return MessageAction.Delete(msg);
        })
        .To(SqsAckSink.Grouped(client, QueueUrl)) // we delete messages in batches
        .Run(_materializer);
}
```

## API

```csharp
namespace Akka.Streams.SQS 
{
    public static class SqsSource
    {
        Source<Message, NotUsed> Create(IAmazonSQS client, string queueUrl, SqsSourceSettings settings = null);
    }

    public static class SqsPublishFlow
    {
        Flow<SendMessageRequest, SqsPublishResult, NotUsed> Default(IAmazonSQS client, string queueUrl, SqsPublishSettings settings = null);
        Flow<SendMessageRequest, SqsPublishResultEntry, NotUsed> Grouped(IAmazonSQS client, string queueUrl, SqsPublishGroupedSettings settings = null);
        Flow<IEnumerable<SendMessageRequest>, IReadOnlyList<SqsPublishResultEntry>, NotUsed> Batch(IAmazonSQS client, string queueUrl, SqsPublishBatchSettings settings = null);
    }

    public static class SqsAckFlow
    {
        Flow<MessageAction, ISqsAckResult, NotUsed> Default(IAmazonSQS client, string queueUrl, SqsAckSettings settings = null);
        Flow<MessageAction, ISqsAckResultEntry, NotUsed> Grouped(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null);
        Flow<Delete, SqsDeleteResultEntry, NotUsed> GroupedDelete(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null);
        Flow<ChangeMessageVisibility, SqsChangeMessageVisibilityResultEntry, NotUsed> GroupedChangeMessageVisibility(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null);
    }

    public static class SqsPublishSink
    {
        Sink<string, Task> Default(IAmazonSQS client, string queueUrl, SqsPublishSettings settings = null);
        Sink<SendMessageRequest, Task> MessageSink(IAmazonSQS client, string queueUrl, SqsPublishSettings settings = null);
        Sink<string, Task> Grouped(IAmazonSQS client, string queueUrl, SqsPublishGroupedSettings settings = null);
        Sink<SendMessageRequest, Task> GroupedMessageSink(IAmazonSQS client, string queueUrl, SqsPublishGroupedSettings settings = null);
        Sink<IEnumerable<string>, Task> Batch(IAmazonSQS client, string queueUrl, SqsPublishBatchSettings settings = null);
        Sink<IEnumerable<SendMessageRequest>, Task> BatchedMessageSink(IAmazonSQS client, string queueUrl, SqsPublishBatchSettings settings = null);
    }
    
    public static class SqsAckSink
    {
        Sink<MessageAction, Task> Default(IAmazonSQS client, string queueUrl, SqsAckSettings settings = null);
        Sink<MessageAction, Task> Grouped(IAmazonSQS client, string queueUrl, SqsAckGroupedSettings settings = null);
    }
}
```

All streams accept `IAmazonSQS` client, assuming that client connection is already authorized and open. Stream don't take ownership over client, meaning it's free to be used in other contexts, but also it must be disposed explicitly by developer.

### Messages

An API defines few custom message types. 

The input events are respresented with `MessageAction` abstract class having several subtypes:

- `Delete` request used to delete corresponding SQS message from the queue. Useful, when you want to remove received messages after they already have been handled and should not longer stay on the queue.
- `ChangeMessageVisibility` request used to prolong the visibility timeout on the SQS queue. Visibility timeout can be used on received messages to prevent them from being visible/requested by other receipients (and therefore reducing multiple receivers and head of line blocking) for a given amount of time.
- `Ignore` which works as simple passthrough wrapper for incomming SQS messages.

Output events are represented by `ISqsResult` interface, implemented by several subtypes:

- `SqsPublishResult` used to inform about result of publishing a single message to SQS via `SqsPublishFlow.Default` stream.
- `SqsPublishResultEntry` used to inform about mutliple partial results of a batched publications (`SqsPublishFlow.Grouped`/`SqsPublishFlow.Batch`).
- `ISqsAckResult` interface, used by `SqsAckFlow`:
    - `SqsDeleteResult` informing about result of corresponding `Delete` action.
    - `SqsChangeMessageVisibilityResult` informing about result of corresponding `ChangeMessageVisibility` action.
    - `SqsIgnoreResult` informing about result of corresponding `Ignore`.