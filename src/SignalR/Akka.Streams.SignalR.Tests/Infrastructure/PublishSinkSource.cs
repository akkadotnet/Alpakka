using Akka.Streams.Dsl;

namespace Akka.Streams.SignalR.AspNetCore.Tests.Infrastructure
{
    public interface IPublishSinkSource
    {
        void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink);
    }
}
