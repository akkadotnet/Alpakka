using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.SQS;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

internal static class Program
{
    private static void Main()
    {
        using var client = new AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig
            { ServiceURL = "http://unit.test", AuthenticationRegion = "us-east-1" });
        const string queue = "http://unit.test/000000000000/old-consumer";
        var defaults = SqsSourceSettings.Default;
        if (defaults.WaitTime != TimeSpan.FromSeconds(20) || defaults.MaxBatchSize != 10
            || defaults.MaxBufferSize != 100 || defaults.Parallelism != 1 || defaults.CloseOnEmptyReceive)
            throw new InvalidOperationException("Retained source defaults changed.");
        var configured = defaults.WithWaitTime(TimeSpan.Zero).WithMaxBatchSize(1).WithMaxBufferSize(1)
            .WithMessageAttributes(new MessageAttributeName("kind")).WithAttributes(AttributeName.All);
        Source<Message, NotUsed> source = SqsSource.Create(client, queue, configured);
        Sink<string, Task<Done>> sink = SqsPublishSink.Default(client, queue);
        Flow<SendMessageRequest, SqsPublishResult, NotUsed> single = SqsPublishFlow.Default(client, queue);
        Flow<SendMessageRequest, SqsPublishResultEntry, NotUsed> grouped = SqsPublishFlow.Grouped(client, queue);
        Flow<IEnumerable<SendMessageRequest>, IReadOnlyList<SqsPublishResultEntry>, NotUsed> batch = SqsPublishFlow.Batch(client, queue);
        Flow<MessageAction, ISqsAckResult, NotUsed> ack = SqsAckFlow.Default(client, queue);
        Flow<MessageAction, ISqsAckResultEntry, NotUsed> groupedAck = SqsAckFlow.Grouped(client, queue);
        var message = new Message { MessageId = "old-id", Body = "old-body", ReceiptHandle = "old-receipt" };
        var delete = (Delete)MessageAction.Delete(message);
        var visibility = (ChangeMessageVisibility)MessageAction.ChangeVisibility(message, TimeSpan.FromSeconds(10));
        var request = new SendMessageRequest(queue, message.Body);
        var result = new SqsPublishResultEntry(request, new SendMessageBatchResultEntry { Id = "0", MessageId = message.MessageId }, new ResponseMetadata());
        if (source is null || sink is null || single is null || grouped is null || batch is null || ack is null || groupedAck is null
            || delete.Message != message || visibility.VisibilityTimeout != TimeSpan.FromSeconds(10)
            || result.Request != request || result.Result.MessageId != "old-id" || defaults.MaxBatchSize != 10)
            throw new InvalidOperationException("Retained graph, result or action APIs changed behavior.");
        Console.WriteLine("Previously compiled SQS consumer passed (factories, settings, result and action APIs).");
    }
}
