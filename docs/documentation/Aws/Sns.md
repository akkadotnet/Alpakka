# Akka Streams SNS Connector

The AWS SNS connector provides an Akka Stream Flow and Sink for push notifications through [Amazon Simple Notification Service (SNS)](https://aws.amazon.com/sns).

## Producer

Sources provided by this connector need a prepared `IAmazonSimpleNotificationService` to publish messages to a topic.

```C#
var credentials = new BasicAWSCredentials("x", "y");
var snsService = new AmazonSimpleNotificationServiceClient(credentials);
```

### Publish messages to a SNS topic
Now we can publish a String message to any SNS topic where we have access to by providing the topic ARN to the `SnsPublisher` Flow or Sink factory method.

### Using a Flow

```C#
Task flow = Source
    .Single("message")
    .Via(SnsPublisher.PlainFlow("topic-arn", snsService))
    .RunWith(Sink.Ignore<PublishResponse>(), _materializer);
```

As you can see, this would publish the messages from the source to the specified AWS SNS topic. After a message has been successfully published, a `PublishResponse` will be pushed downstream.

```C#
Task sink = Source
    .Single("message")
    .RunWith(SnsPublisher.PlainSink("topic-arn", snsService), _materializer);
```
As you can see, this would publish the messages from the source to the specified AWS SNS topic.