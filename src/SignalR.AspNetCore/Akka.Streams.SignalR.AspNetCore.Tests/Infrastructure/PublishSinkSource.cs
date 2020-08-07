using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;

namespace Akka.Streams.SignalR.AspNetCore.Tests.Infrastructure
{
    public interface IPublishSinkSource
    {
        void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink);
    }

    public class PublishSinkSource : IPublishSinkSource
    {
        private readonly ActorSystem _system;
        private readonly TestKitBase _testKit;
        public int Flows { get; set; } = 1;

        public TestSubscriber.Probe<ISignalREvent> FromClient { get; private set; }
        public TestPublisher.Probe<ISignalRResult> ToClient { get; private set; }
        public TestSubscriber.Probe<ISignalREvent> FromClient2 { get; private set; }
        public TestPublisher.Probe<ISignalRResult> ToClient2 { get; private set; }

        public PublishSinkSource(ActorSystem system, TestKitBase testKit)
        {
            _system = system;
            _testKit = testKit;
        }

        public void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink)
        {
            FromClient = source.RunWith(_testKit.SinkProbe<ISignalREvent>(), _system.Materializer());
            ToClient = sink.RunWith(_testKit.SourceProbe<ISignalRResult>(), _system.Materializer());

            if (Flows > 1)
            {
                FromClient2 = source.RunWith(_testKit.SinkProbe<ISignalREvent>(), _system.Materializer());
                ToClient2 = sink.RunWith(_testKit.SourceProbe<ISignalRResult>(), _system.Materializer());
            }
        }
    }
}
