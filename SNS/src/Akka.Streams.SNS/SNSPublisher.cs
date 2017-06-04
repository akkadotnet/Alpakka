using Akka.Streams.Dsl;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
namespace Akka.Streams.SNS
{
    public static class SnsPublisher
    {
        public static Flow<string, PublishResponse, NotUsed> PublishToSNSFlow(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            return Flow.FromGraph(new SNSPublishFlowStage(topicArn, snsService));
        }
    }
}