using Akka.Streams.Dsl;
using Akka.Streams.SignalR;

namespace SignalRSample
{
    public class EchoConnection : StreamConnection
    {
        public EchoConnection()
        {
            this.Source
                .Select(x => Signals.Broadcast(x.Data))
                .To(this.Sink)
                .Run(App.Materializer);
        }
    }
}