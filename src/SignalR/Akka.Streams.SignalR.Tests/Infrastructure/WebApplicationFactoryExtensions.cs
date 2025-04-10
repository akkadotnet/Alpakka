using Microsoft.AspNetCore.Mvc.Testing;
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
                })
                .Build();

            return client;
        }
    }
}
