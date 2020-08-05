using System;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.Tests.Internals;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hosting;
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
            //var connectionContext = GlobalHost.ConnectionManager.GetConnectionContext<TestConnection>();
            //var factory = new PersistentConnectionFactory(GlobalHost.DependencyResolver);
            //connection = (TestConnection)factory.CreateInstance(typeof(TestConnection));
            //connection.Initialize(GlobalHost.DependencyResolver);
        }

        [Fact]
        public void Websocket_connection_should_be_able_to_receive_data()
        {
            //throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_connection_should_be_able_to_send_data()
        {
            //throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_fail_when_the_server_fails()
        {
            //throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_work_when_client_closes()
        {
            //throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_work_when_client_fails()
        {
            //throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_work_when_used_by_multiple_flows()
        {
            //throw new NotImplementedException();
        }

        [Fact]
        public void Websocket_stream_should_shutdown_on_demand()
        {
            //throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            server.Dispose();
            materializer.Dispose();
        }
    }
}