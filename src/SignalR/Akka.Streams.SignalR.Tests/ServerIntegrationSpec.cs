using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.Tests.Infrastructure;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.SignalR.Tests
{
    public class ServerIntegrationSpec : 
        Akka.TestKit.Xunit2.TestKit, 
        IPublishSinkSource,
        IAsyncLifetime
    {
        private readonly TestServer _testServer;

        private volatile bool _connected;
        private readonly List<HubConnection> _connections = new List<HubConnection>();
        private HubConnection _connection;

        private TestSubscriber.Probe<ISignalREvent> _fromClient;
        private TestPublisher.Probe<ISignalRResult> _toClient;
        private TestSubscriber.Probe<ISignalREvent> _fromClient2;
        private TestPublisher.Probe<ISignalRResult> _toClient2;

        private Action<Source<ISignalREvent, NotUsed>, Sink<ISignalRResult, NotUsed>> _connectCallback;

        public ServerIntegrationSpec(
            ITestOutputHelper output)
            : base(system: null, output: output)
        {
            _testServer = new TestServer(new WebHostBuilder()
                .UseSolutionRelativeContentRoot(AppContext.BaseDirectory, "*.slnx")
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
                }));
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_receive_data()
        {
            // Arrange
            await ConnectAsync(msg => Log.Info(msg));

            // Act
            await _connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            // Assert
            Assert.IsType<Connected>(_fromClient.RequestNext());
            var message = _fromClient.RequestNext();
            Assert.IsType<Received>(message);
            Assert.Equal("payload", ((Received)message).Data.ToString());
        }

        [Fact]
        public async Task Websocket_connection_should_be_able_to_send_data()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            await ConnectAsync(msg => tcs.TrySetResult(msg));

            // Act
            _toClient.SendNext(Signals.Broadcast("payload"));

            // Assert
            await AwaitAssertAsync(() => Assert.True(tcs.Task.IsCompleted));
            var result = await tcs.Task;
            Assert.Equal("payload", result);
        }

        [Fact]
        public async Task Websocket_stream_should_work_when_client_closes()
        {
            await ConnectAsync(msg => Log.Info(msg));
            Assert.IsType<Connected>(_fromClient.RequestNext());

            await _connection.InvokeAsync(nameof(IServerSource.Send), "payload");
            Assert.IsType<Received>(_fromClient.RequestNext());

            // Client-initiated disconnect
            _connected = false;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connections.Remove(_connection);
            _connection = null;

            // Server should know
            Assert.IsType<Disconnected>(_fromClient.RequestNext());

            // Client reconnects to server
            var connection = CreateConnection();
            connection.On<string>(nameof(IClientSink.Receive), msg => Log.Info(msg));
            await connection.StartAsync();

            // Server should see new connection
            Assert.IsType<Connected>(_fromClient.RequestNext());

            // Send new message
            await connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            Assert.IsType<Received>(_fromClient.RequestNext());
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

            Assert.IsType<Connected>(data1);
            Assert.IsType<Connected>(data2);

            await _connection.InvokeAsync(nameof(IServerSource.Send), "payload");

            data1 = _fromClient.RequestNext();
            data2 = _fromClient2.RequestNext();

            Assert.IsType<Received>(data1);
            Assert.IsType<Received>(data2);

            Assert.Equal("payload", ((Received)data1).Data.ToString());
            Assert.Equal("payload", ((Received)data2).Data.ToString());

        }

        private async Task ConnectAsync(Action<string> handler)
        {
            _connection = CreateConnection();
            _connection.On<string>(nameof(IClientSink.Receive), handler);
            await _connection.StartAsync();

            await AwaitAssertAsync(() => Assert.True(_connected), TimeSpan.FromSeconds(5));
        }

        private HubConnection CreateConnection()
        {
            var connection = _testServer.CreateHubConnection();
            _connections.Add(connection);
            return connection;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var failures = new List<Exception>();
            foreach (var connection in _connections)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await connection.StopAsync(timeout.Token);
                }
                catch (Exception error) { failures.Add(error); }
                try { await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception error) { failures.Add(error); }
            }
            try { _testServer.Dispose(); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("SignalR fixture cleanup failed.", failures);
        }

        public void Connect(Source<ISignalREvent, NotUsed> source, Sink<ISignalRResult, NotUsed> sink)
        {
            _fromClient = source.RunWith(this.SinkProbe<ISignalREvent>(), Sys.Materializer());
            _toClient = sink.RunWith(this.SourceProbe<ISignalRResult>(), Sys.Materializer());

            _connectCallback?.Invoke(source, sink);
            // A probe subscription arrives after the source stage has subscribed to connector events.
            _fromClient.EnsureSubscription();
            _fromClient2?.EnsureSubscription();
            _connected = true;
        }
    }

}
