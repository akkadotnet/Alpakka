using Akka.Streams.Dsl;
using Akka.Streams.SignalR;

namespace SignalRSample
{
    public class EchoConnection : StreamConnection
    {
        public EchoConnection()
        {
            this.Source
                .Collect(x => x as Received)
                .Select(x => Signals.Broadcast(x.Data))
                .To(this.Sink)
                .Run(App.Materializer);
        }
    }
}