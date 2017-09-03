using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;

namespace Akka.Streams.Azure.EventHub
{
    internal interface IHubClient
    {
        Task SendBatchAsync(IEnumerable<EventData> events);
    }
    
    internal sealed class HubClientWrapper : IHubClient
    {
        private readonly EventHubClient _client;

        public HubClientWrapper(EventHubClient client)
        {
            _client = client;
        }

        public Task SendBatchAsync(IEnumerable<EventData> events) => _client.SendAsync(events);
    }
}