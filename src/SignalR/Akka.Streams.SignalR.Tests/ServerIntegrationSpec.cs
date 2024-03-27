using System;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.Tests.Infrastructure;
using Akka.Streams.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SignalR.Tests
{
    public class ServerIntegrationSpec : 
        Akka.TestKit.Xunit2.TestKit, 
        IPublishSinkSource,
        IClassFixture<TestServerAppFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;

        private bool _connected;
        private HubConnection _connection;

        private TestSubscriber.Probe<ISignalREvent> _fromClient;
        private TestPublisher.Probe<ISignalRResult> _toClient;
        private TestSubscriber.Probe<ISignalREvent> _fromClient2;
        private TestPublisher.Probe<ISignalRResult> _toClient2;

        private Action<Source<ISignalREvent, NotUsed>, Sink<ISignalRResult, NotUsed>> _connectCallback;

        public ServerIntegrationSpec(
            ITestOutputHelper output,
            TestServerAppFactory factory)
            : base(system: null, output: output)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder
                    .UseContentRoot("")
                    .ConfigureServices(services =>
                    {
                        services
                            .AddSingleton<IPublishSinkSource>(this)
                            .AddSingleton(Sys)
                            .AddSingleton(this)
                            .AddSignalRAkkaStream()
                            .AddSignalR(opt => opt.EnableDetailedErrors = true);
                    })
                    .Configure(app =>
                    {
                        app
                            .UseRouting()
                            .UseEndpoints(config =>
                            {
                                config.MapHub<TestStreamHub>("/test");
                            });
                    });
            });
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_receive_data()
        {
            // Arrange
            await ConnectAsync(msg => Log.Info(msg));

            // Act
            await _connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            // Assert
            _fromClient.RequestNext().Should().BeOfType<Connected>();
            var message = _fromClient.RequestNext();
            message.Should().BeOfType<Received>();
            ((Received)message).Data.ToString().Should().Be("payload");
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_send_data()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>();
            await ConnectAsync(msg => tcs.SetResult(msg));

            // Act
            _toClient.SendNext(Signals.Broadcast("payload"));

            // Assert
            var result = await tcs.Task;
            result.Should().Be("payload");
        }

        [Fact]
        public async Task Websocket_stream_should_work_when_client_closes()
        {
            await ConnectAsync(msg => Log.Info(msg));
            _fromClient.RequestNext().Should().BeOfType<Connected>();

            await _connection.InvokeAsync(nameof(IServerSource.Send), "payload");
            _fromClient.RequestNext().Should().BeOfType<Received>();

            // Client-initiated disconnect
            _connected = false;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;

            // Server should know
            _fromClient.RequestNext().Should().BeOfType<Disconnected>();

            // Client reconnects to server
            var connection = _factory.CreateHubConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            await connection.StartAsync();

            // Server should see new connection
            _fromClient.RequestNext().Should().BeOfType<Connected>();

            // Send new message
            await connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            _fromClient.RequestNext().Should().BeOfType<Received>();
        }

        [Fact]
        public async Task Websocket_stream_should_work_when_used_by_multiple_flows()
        {
            // Arrange
            _connectCallback = (source, sink) =>
            {
                _fromClient2 = source.RunWith(this.SinkProbe<ISignalREvent>(), Sys.Materializer());
                _toClient2 = sink.RunWith(this.SourceProbe<ISignalRResult>(), Sys.Materializer());
            };

            await ConnectAsync(msg => Log.Info(msg));

            var data1 = _fromClient.RequestNext();
            var data2 = _fromClient2.RequestNext();

            data1.Should().BeOfType<Connected>();
            data2.Should().BeOfType<Connected>();

            await _connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            data1 = _fromClient.RequestNext();
            data2 = _fromClient2.RequestNext();

            data1.Should().BeOfType<Received>();
            data2.Should().BeOfType<Received>();

            ((Received)data1).Data.ToString().Should().Be("payload");
            ((Received)data2).Data.ToString().Should().Be("payload");

        }

        private async Task ConnectAsync(Action<string> handler)
        {
            _connection = _factory.CreateHubConnection();
            _connection.On<string>(nameof(IClientSink.Receive), handler);
            await _connection.StartAsync();

            var deadline = DateTime.Now + TimeSpan.FromSeconds(5);
            while (DateTime.Now < deadline)
            {
                await Task.Delay(50);
                if (_connected)
                    return;
            }

            throw new Exception("Connection timeout");
        }

        public void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink)
        {
            _fromClient = source.RunWith(this.SinkProbe<ISignalREvent>(), Sys.Materializer());
            _toClient = sink.RunWith(this.SourceProbe<ISignalRResult>(), Sys.Materializer());

            _connectCallback?.Invoke(source, sink);
            _connected = true;
        }
    }

}