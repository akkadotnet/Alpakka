using System;
using Akka.Streams.SignalR.Tests.Internals;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SignalR.Tests
{
    public class ServerIntegrationSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer materializer;
        private readonly StreamConnection connection;
        private readonly TestServer server;

        public ServerIntegrationSpec(ITestOutputHelper output) : base(output: output)
        {
            materializer = Sys.Materializer();
            server = TestHelpers.CreateServer();
        }

        [Fact]
        public void Websocket_connection_should_be_able_to_send_and_receive_bytes()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_fail_when_the_server_fails()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_work_when_client_closes()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_work_when_client_fails()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_work_when_used_by_multiple_flows()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_shutdown_on_demand()
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            server.Dispose();
            materializer.Dispose();
        }
    }
}