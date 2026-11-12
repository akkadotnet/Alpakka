using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace Akka.Streams.SignalR.Tests.Infrastructure
{
    public static class WebApplicationFactoryExtensions
    {
        public static HubConnection CreateHubConnection(this TestServer factory)
        {
            var client = new HubConnectionBuilder()
                .WithUrl($"{factory.BaseAddress}test", opt => {
                    opt.HttpMessageHandlerFactory = _ => factory.CreateHandler();
                    opt.Transports = HttpTransportType.WebSockets;
                    opt.SkipNegotiation = true;
                    opt.WebSocketFactory = (context, token) => new ValueTask<WebSocket>(factory.CreateWebSocketClient().ConnectAsync(context.Uri, token));
                })
                .Build();

            return client;
        }
    }
}
