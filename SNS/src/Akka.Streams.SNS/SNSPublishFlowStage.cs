using Akka.Streams.Stage;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System;
using System.Threading.Tasks;

namespace Akka.Streams.SNS
{
    public class SNSPublishFlowStage: GraphStage<FlowShape<string, PublishResponse>>
    {
        private string topicArn;
        private IAmazonSimpleNotificationService snsService;
private readonly Inlet<string> inlet =new Inlet<string>("SnsPublishFlow.in");
        private readonly Outlet<PublishResponse> outlet =new Outlet<PublishResponse>("SnsPublishFlow.out");

        public SNSPublishFlowStage(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            this.topicArn = topicArn;
            this.snsService = snsService;
        }
        
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new logic(this, topicArn, snsService);
        }
        #region logic

        private sealed class logic : InAndOutGraphStageLogic
        {
            private bool isMessageInFlight = false;
            private IAmazonSimpleNotificationService snsService;
            private string topicArn;
            private SNSPublishFlowStage stage;
            private Action<PublishResponse> onSuccess;
            private Action<Exception> onFailure;
            
            public logic(SNSPublishFlowStage stage, string topicArn, IAmazonSimpleNotificationService snsService)
                : base(stage.Shape)
            {
                this.stage = stage;
                this.topicArn = topicArn;
                this.snsService = snsService;
                this.onSuccess = GetAsyncCallback<PublishResponse>(HandleSuccess);
                this.onFailure = GetAsyncCallback<Exception>(HandleFailure);
            }

            private void HandleFailure(Exception ex)
            {
                FailStage(ex);
            }

            private void HandleSuccess(PublishResponse response)
            {
                //add this when 1.3 comes out.
                //log.debug("Published SNS message: {}", result.getMessageId)
                isMessageInFlight = false;
                if (!IsClosed(stage.outlet)) 
                    Push(stage.outlet, response);
            }

            public override void OnPush()
            {
                isMessageInFlight = true;
                var request = new PublishRequest();
                request.TopicArn = topicArn;
                request.Message = Grab(stage.inlet);
                snsService.PublishAsync(request)
                    .ContinueWith(
                        task =>
                            {
                                if (task.IsFaulted || task.IsCanceled)
                                    onFailure(task.Exception);
                                else
                                    onSuccess(task.Result);
                            });
            }
            
            public override void OnPull()
            {
                if (IsClosed(stage.inlet) && !isMessageInFlight) CompleteStage();
                if (!HasBeenPulled(stage.inlet)) TryPull(stage.inlet);
            }
            
            public override void OnUpstreamFinish()
            {
                if (!isMessageInFlight) CompleteStage();

                SetHandler(stage.inlet, this);
                SetHandler(stage.outlet, this);
            }
        }
        #endregion

        public override FlowShape<string, PublishResponse> Shape
        {
            get { return new FlowShape<string,PublishResponse>(inlet, outlet); }
        }
    }
}