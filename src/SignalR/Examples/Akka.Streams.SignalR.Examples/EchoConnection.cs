using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR;
using Akka.Streams.SignalR.AspNetCore;
using Akka.Streams.SignalR.AspNetCore.Internals;
using Microsoft.AspNetCore.SignalR;

namespace SignalRSample
{
    public class EchoHub : StreamHub<EchoStream>
    {
        public EchoHub(IStreamDispatcher dispatcher)
            : base(dispatcher)
        { }
    }

    public class EchoStream : StreamConnector
    {
        public EchoStream(IHubClients clients, ConnectionSourceSettings sourceSettings = null, ConnectionSinkSettings sinkSettings = null) 
            : base(clients, sourceSettings, sinkSettings)
        {
            Source
                .Collect(x => x as Received)
                // Tell everyone
                .Select(x => Signals.Broadcast(x.Data))
                // Or tell everyone else, except one-self
                // .Select(x => Signals.Broadcast(x.Data, new[] { x.Request.ConnectionId }))
                // Or just send it back to one-self
                // .Select(x => Signals.Send(x.Request.ConnectionId, x.Data.Payload))
                .To(Sink)
                .Run(App.Materializer);
        }
    }
}