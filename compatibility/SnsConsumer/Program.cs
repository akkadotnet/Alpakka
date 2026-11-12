using System;
using System.Threading.Tasks;
using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.Sns;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

internal static class Program
{
    private static void Main()
    {
        using var client = new AmazonSimpleNotificationServiceClient(new BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig { ServiceURL = "http://unit.test", AuthenticationRegion = "us-east-1" });
        const string topic = "arn:aws:sns:us-east-1:000000000000:old-consumer";
        Flow<string, PublishResponse, NotUsed> flow = SnsPublisher.PlainFlow(topic, client);
        Sink<string, Task<Done>> sink = SnsPublisher.PlainSink(topic, client);
        var response = new PublishResponse { MessageId = "old-id" };
        if (flow is null || sink is null || response.MessageId != "old-id")
            throw new InvalidOperationException("Retained SNS graph or response APIs changed behavior.");
        Console.WriteLine("Previously compiled SNS consumer passed (factory and SDK response APIs).");
    }
}
