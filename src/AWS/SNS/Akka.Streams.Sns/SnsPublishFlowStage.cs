using Akka.Event;
using Akka.Streams.Stage;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System;

namespace Akka.Streams.Sns
{
    internal class SnsPublishFlowStage: GraphStage<FlowShape<string, PublishResponse>>
    {
        public string TopicArn { get; }
        public IAmazonSimpleNotificationService SnsService { get; }

        public Inlet<string> In { get; } = new Inlet<string>("SnsPublishFlow.in");
        public Outlet<PublishResponse> Out { get; } = new Outlet<PublishResponse>("SnsPublishFlow.out");
        public override FlowShape<string, PublishResponse> Shape { get; }

        public SnsPublishFlowStage(string topicArn, IAmazonSimpleNotificationService snsService)
        {
            TopicArn = topicArn;
            SnsService = snsService;
            Shape = new FlowShape<string, PublishResponse>(In, Out);
        }
        
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new SnsPublishFlowStageLogic(this, inheritedAttributes);
        }

        private sealed class SnsPublishFlowStageLogic : InAndOutGraphStageLogic
        {
            private readonly SnsPublishFlowStage _stage;
            private bool _isMessageInFlight;
            private readonly Action<Exception> _onFailure;
            private readonly Action<PublishResponse> _onSuccess;

            public SnsPublishFlowStageLogic(SnsPublishFlowStage stage, Attributes inheritedAttributes)
                : base(stage.Shape)
            {
                _stage = stage;
                _onFailure = GetAsyncCallback<Exception>(HandleFailure);
                _onSuccess = GetAsyncCallback<PublishResponse>(HandleSuccess);
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            private void HandleFailure(Exception ex)
            {
                FailStage(ex);
            }

            private void HandleSuccess(PublishResponse result)
            {
                Log.Debug($"Published SNS message: {result.MessageId}");
                _isMessageInFlight = false;
                if (!IsClosed(_stage.Out))
                {
                    Push(_stage.Out, result);
                }
            }

            public override void OnPush()
            {
                _isMessageInFlight = true;
                var request = new PublishRequest(_stage.TopicArn, Grab(_stage.In));
                _stage.SnsService.PublishAsync(request)
                    .ContinueWith(
                        task =>
                            {
                                if (task.IsFaulted || task.IsCanceled)
                                    _onFailure(task.Exception);
                                else
                                    _onSuccess(task.Result);
                            });
            }
            
            public override void OnPull()
            {
                if (IsClosed(_stage.In) && !_isMessageInFlight)
                {
                    CompleteStage();
                }

                if (!HasBeenPulled(_stage.In))
                {
                    TryPull(_stage.In);
                }
            }
            
            public override void OnUpstreamFinish()
            {
                if (!_isMessageInFlight)
                {
                    CompleteStage();
                }
            }
        }
    }
}