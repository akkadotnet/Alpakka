using Akka.Streams.Dsl;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Threading.Tasks;

namespace Akka.Streams.SNS
{
    public static class SnsPublisher
    {
        public static Flow<string, PublishResponse, NotUsed> PublishToSNSFlow(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            return Flow.FromGraph(new SNSPublishFlowStage(topicArn, snsService));
        }

        public static Sink<string, Task> PublishToSNSSink(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            return PublishToSNSFlow(topicArn, snsService).ToMaterialized(Sink.Ignore<PublishResponse>(), Keep.Right);
        }
    }
}