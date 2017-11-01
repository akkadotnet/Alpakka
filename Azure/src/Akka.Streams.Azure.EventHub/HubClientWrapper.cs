using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;

namespace Akka.Streams.Azure.EventHub
{
    internal interface IHubClient
    {
        Task SendBatchAsync(IEnumerable<EventData> events);
    }
    
    internal sealed class HubSenderWrapper : IHubClient
    {
        private readonly EventHubSender _sender;

        public HubSenderWrapper(EventHubSender sender)
        {
            _sender = sender;
        }

        public Task SendBatchAsync(IEnumerable<EventData> events) => _sender.SendBatchAsync(events);
    }

    internal sealed class HubClientWrapper : IHubClient
    {
        private readonly EventHubClient _client;

        public HubClientWrapper(EventHubClient client)
        {
            _client = client;
        }

        public Task SendBatchAsync(IEnumerable<EventData> events) => _client.SendBatchAsync(events);
    }
}