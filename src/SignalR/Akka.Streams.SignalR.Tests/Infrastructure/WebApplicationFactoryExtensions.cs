using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Akka.Streams.SignalR.Tests.Infrastructure
{
    public static class WebApplicationFactoryExtensions
    {
        public static HubConnection CreateHubConnection<T>(this WebApplicationFactory<T> factory) where T : class
        {
            var client = new HubConnectionBuilder()
                .WithUrl($"{factory.Server.BaseAddress}test", opt => {
                    opt.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
                .Build();

            return client;
        }
    }
}
