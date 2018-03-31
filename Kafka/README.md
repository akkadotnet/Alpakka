# Akka Streams Kafka

Akka Streams Kafka is an Akka Streams connector for Apache Kafka.

## Builds
[![Build status](https://ci.appveyor.com/api/projects/status/0glh2fi8uic17vl4/branch/dev?svg=true)](https://ci.appveyor.com/project/akkadotnet-contrib/akka-streams-kafka/branch/dev)

## Producer

A producer publishes messages to Kafka topics. The message itself contains information about what topic and partition to publish to so you can publish to different topics with the same producer.

### Settings

When creating a consumer stream you need to pass in `ProducerSettings` that define things like:

- bootstrap servers of the Kafka cluster
- serializers for the keys and values
- tuning parameters

```C#
var producerSettings = new ProducerSettings<Null, string>(system, null, new StringSerializer(Encoding.UTF8))
    .WithBootstrapServers("localhost:9092");
```

### Producer as a Sink
`Producer.PlainSink` is the easiest way to publish messages. The sink consumes `ProducerRecord` elements which contains a topic name to which the record is being sent.

```C#
Source
    .From(Enumerable.Range(500, 601))
    .Select(c => c.ToString())
    .Select(elem => new MessageAndMeta<Null, string> { Topic = "topic1", Message = new Message<Null, string> { Value = elem }})
    .RunWith(Producer.PlainSink(producerSettings), materializer);
```
The materialized value of the sink is a `Task` which is completed with result when the stream completes or with exception if an error occurs.

### Producer as a Flow
Sometimes there is a need for publishing messages in the middle of the stream processing, not as the last step, and then you can use `Producer.CreateFlow`

```C#
Source
    .From(Enumerable.Range(1, 100))
    .Select(c => c.ToString())
    .Select(elem => new MessageAndMeta<Null, string> { Topic = "topic1", Message = new Message<Null, string> { Value = elem }})
    .Via(Producer.CreateFlow(producerSettings))
    .Select(record =>
    {
        Console.WriteLine($"Producer: {record.Topic}/{record.Partition} {record.Offset}: {record.Value}");
        return record;
    })
    .RunWith(Sink.Ignore<Message<Null, string>>(), materializer);
```

## Consumer

A consumer is used for subscribing to Kafka topics.

### Settings 
When creating a consumer stream you need to pass in `ConsumerSettings` that define things like:

- bootstrap servers of the Kafka cluster
- group id for the consumer, note that offsets are always committed for a given consumer group
- serializers for the keys and values
- tuning parameters

```C#
var consumerSettings = ConsumerSettings<Null, string>.Create(system, null, new StringDeserializer(Encoding.UTF8))
    .WithBootstrapServers("localhost:9092")
    .WithGroupId("group1");
```

### Plain Consumer
```C#
var subscription = Subscriptions.Assignment(new TopicPartition("akka", 0));

Consumer.PlainSource(consumerSettings, subscription)
    .RunForeach(result =>
    {
        Console.WriteLine($"Consumer: {result.Topic}/{result.Partition} {result.Offset}: {result.Value}");
    }, materializer);
```

### Committable Consumer
The `Consumer.CommittableSource` makes it possible to commit offset positions to Kafka.

Compared to auto-commit this gives exact control of when a message is considered consumed.

If you need to store offsets in anything other than Kafka, `PlainSource` should be used instead of this API.

This is useful when “at-least once delivery” is desired, as each message will likely be delivered one time but in failure cases could be duplicated.

```C#
Consumer.CommitableSource(consumerSettings, Subscriptions.Topics("topic1"))
    .SelectAsync(1, elem =>
    {
        return elem.CommitableOffset.Commit();
    })
    .RunWith(Sink.Ignore<CommittedOffsets>(), _materializer);
```
The above example uses separate mapAsync stages for processing and committing. This guarantees that for parallelism higher than 1 we will keep correct ordering of messages sent for commit.

Committing the offset for each message as illustrated above is rather slow. It is recommended to batch the commits for better throughput, with the trade-off that more messages may be re-delivered in case of failures.