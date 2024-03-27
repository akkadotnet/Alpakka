using Akka.Streams.SignalR.Internals;
using Microsoft.AspNetCore.SignalR;

namespace Akka.Streams.SignalR.Tests.Infrastructure
{
    /// <summary>
    /// Hubs rely on DI to get references.
    /// </summary>
    public class TestStreamHub : StreamHub<TestStreamConnector>
    {
        public TestStreamHub(IStreamDispatcher dispatcher) : base(dispatcher)
        { }
    }

    public class TestStreamConnector : StreamConnector
    {
        public TestStreamConnector(
            IPublishSinkSource connect,
            IHubClients clients,
            ConnectionSourceSettings sourceSettings = null, 
            ConnectionSinkSettings sinkSettings = null) 
            : base(clients, sourceSettings, sinkSettings)
        {
            // Connect source and sink to external sink probes
            connect.Connect(Source, Sink);
        }
    }
}
