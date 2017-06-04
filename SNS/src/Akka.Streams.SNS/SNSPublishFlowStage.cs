using Akka.Streams.Stage;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System;

namespace Akka.Streams.SNS
{
    public class SNSPublishFlowStage: GraphStage<FlowShape<string, PublishResponse>>
    {
        private string topicArn;
        private IAmazonSimpleNotificationService snsService;

        public SNSPublishFlowStage(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            this.topicArn = topicArn;
            this.snsService = snsService;
        }
        
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            throw new NotImplementedException();
        }

        public override FlowShape<string, PublishResponse> Shape
        {
            get { throw new NotImplementedException(); }
        }
    }
}