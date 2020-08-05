using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SignalR.AspNetCore.Tests
{
    public class ServerIntegrationSpec : Akka.TestKit.Xunit2.TestKit
    {

        public ServerIntegrationSpec(ITestOutputHelper output)
            : base(system: null, output: output)
        { }

        [Fact]
        public void Websocket_connection_should_be_able_to_receive_data()
        {
            // Arrange
            var factory = new TestServerAppFactory(Sys, this);
            var connection = factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            connection.StartAsync().Wait();
            Task.Delay(500).Wait();

            // Act
            connection.InvokeAsync(nameof(IServerSource.Send), "payload");
            
            // Assert
            factory.FromClient.RequestNext().Should().BeOfType<Connected>();
            var message = factory.FromClient.RequestNext();
            message.Should().BeOfType<Received>();
            ((Received)message).Data.Should().Be("payload");
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_send_data()
        {
            // Arrange
            var factory = new TestServerAppFactory(Sys, this);
            var tcs = new TaskCompletionSource<object>();
            var connection = factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => tcs.SetResult(msg));
            connection.StartAsync().Wait();
            Task.Delay(500).Wait();

            // Act
            factory.ToClient.SendNext(Signals.Broadcast("payload"));

            // Assert
            var result = await tcs.Task;
            result.Should().Be("payload");
        }

        [Fact]
        public void Websocket_stream_should_work_when_client_closes()
        {
            var factory = new TestServerAppFactory(Sys, this);
            var connection = factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            connection.StartAsync().Wait();
            Task.Delay(500).Wait();

            connection.InvokeAsync(nameof(IServerSource.Send), "payload");
            factory.FromClient.RequestNext().Should().BeOfType<Connected>();
            factory.FromClient.RequestNext().Should().BeOfType<Received>();

            // Client-initiated disconnect
            connection.DisposeAsync().Wait();

            // Server should know
            factory.FromClient.RequestNext().Should().BeOfType<Disconnected>();

            // Client reconnects to server
            connection = factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            connection.StartAsync().Wait();

            // Server should see new connection
            factory.FromClient.RequestNext().Should().BeOfType<Connected>();

            // Send new message
            connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            factory.FromClient.RequestNext().Should().BeOfType<Received>();
        }

        [Fact]
        public void Websocket_stream_should_work_when_used_by_multiple_flows()
        {
            // Arrange
            // Arrange
            var factory = new TestServerAppFactory(Sys, this, 2);
            var connection = factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            connection.StartAsync().Wait();
            Task.Delay(500).Wait();

            var data1 = factory.FromClient.RequestNext();
            var data2 = factory.FromClient2.RequestNext();

            data1.Should().BeOfType<Connected>();
            data2.Should().BeOfType<Connected>();

            connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            data1 = factory.FromClient.RequestNext();
            data2 = factory.FromClient2.RequestNext();

            data1.Should().BeOfType<Received>();
            data2.Should().BeOfType<Received>();

            ((Received)data1).Data.Should().Be("payload");
            ((Received)data2).Data.Should().Be("payload");

        }
    }

}