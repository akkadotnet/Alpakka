using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.AspNetCore.Internals;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Streams.SignalR.AspNetCore.Tests
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
