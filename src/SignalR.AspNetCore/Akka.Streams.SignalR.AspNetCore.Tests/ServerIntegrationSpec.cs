using System.Net.Http;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.SignalR.AspNetCore.Tests.Infrastructure;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SignalR.AspNetCore.Tests
{
    public class ServerIntegrationSpec : Akka.TestKit.Xunit2.TestKit, IClassFixture<TestServerAppFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly PublishSinkSource _publishSinkSource;
        private readonly HttpClient _client;

        public ServerIntegrationSpec(
            ITestOutputHelper output,
            TestServerAppFactory factory)
            : base(system: null, output: output)
        {
            _publishSinkSource = new PublishSinkSource(Sys, this);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder
                    .UseContentRoot("")
                    .ConfigureServices(services =>
                    {
                        services.AddSignalRAkkaStream();
                        services.AddSignalR(opt => opt.EnableDetailedErrors = true);
                        services.Add(new ServiceDescriptor(typeof(IPublishSinkSource), _publishSinkSource));
                        services.Add(new ServiceDescriptor(typeof(ActorSystem), Sys));
                        services.Add(new ServiceDescriptor(typeof(TestKitBase), this));
                    })
                    .Configure(app =>
                    {
                        app.UseSignalR(config =>
                        {
                            config.MapHub<TestStreamHub>("/test");
                        });
                    });
            });
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_receive_data()
        {
            // Arrange
            var connection = _factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            await connection.StartAsync();
            await Task.Delay(500);

            // Act
            await connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            // Assert
            _publishSinkSource.FromClient.RequestNext().Should().BeOfType<Connected>();
            var message = _publishSinkSource.FromClient.RequestNext();
            message.Should().BeOfType<Received>();
            ((Received)message).Data.ToString().Should().Be("payload");
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_send_data()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>();
            var connection = _factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => tcs.SetResult(msg));
            await connection.StartAsync();
            await Task.Delay(500);

            // Act
            _publishSinkSource.ToClient.SendNext(Signals.Broadcast("payload"));

            // Assert
            var result = await tcs.Task;
            result.Should().Be("payload");
        }

        [Fact]
        public async Task Websocket_stream_should_work_when_client_closes()
        {
            var connection = _factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            await connection.StartAsync();
            await Task.Delay(500);

            await connection.InvokeAsync(nameof(IServerSource.Send), "payload");
            _publishSinkSource.FromClient.RequestNext().Should().BeOfType<Connected>();
            _publishSinkSource.FromClient.RequestNext().Should().BeOfType<Received>();

            // Client-initiated disconnect
            await connection.DisposeAsync();

            // Server should know
            _publishSinkSource.FromClient.RequestNext().Should().BeOfType<Disconnected>();

            // Client reconnects to server
            connection = _factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            await connection.StartAsync();

            // Server should see new connection
            _publishSinkSource.FromClient.RequestNext().Should().BeOfType<Connected>();

            // Send new message
            await connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            _publishSinkSource.FromClient.RequestNext().Should().BeOfType<Received>();
        }

        [Fact]
        public async Task Websocket_stream_should_work_when_used_by_multiple_flows()
        {
            // Arrange
            _publishSinkSource.Flows = 2;
            var connection = _factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            await connection.StartAsync();
            await Task.Delay(500);

            var data1 = _publishSinkSource.FromClient.RequestNext();
            var data2 = _publishSinkSource.FromClient2.RequestNext();

            data1.Should().BeOfType<Connected>();
            data2.Should().BeOfType<Connected>();

            await connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            data1 = _publishSinkSource.FromClient.RequestNext();
            data2 = _publishSinkSource.FromClient2.RequestNext();

            data1.Should().BeOfType<Received>();
            data2.Should().BeOfType<Received>();

            ((Received)data1).Data.ToString().Should().Be("payload");
            ((Received)data2).Data.ToString().Should().Be("payload");

        }
    }

}