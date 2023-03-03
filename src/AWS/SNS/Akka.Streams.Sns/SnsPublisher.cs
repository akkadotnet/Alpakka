using Akka.Streams.Dsl;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Threading.Tasks;

namespace Akka.Streams.Sns
{
    public static class SnsPublisher
    {
        /// <summary>
        /// Creates a <see cref="Flow"/> to publish messages to a SNS topic using an <see cref="IAmazonSimpleNotificationService"/>
        /// </summary>
        public static Flow<string, PublishResponse, NotUsed> PlainFlow(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            return Flow.FromGraph(new SnsPublishFlowStage(topicArn, snsService));
        }

        /// <summary>
        /// Creates a <see cref="Sink"/> to publish messages to a SNS topic using an <see cref="IAmazonSimpleNotificationService"/>
        /// </summary>
        public static Sink<string, Task<Done>> PlainSink(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            return PlainFlow(topicArn, snsService).ToMaterialized(Sink.Ignore<PublishResponse>(), Keep.Right);
        }
    }
}