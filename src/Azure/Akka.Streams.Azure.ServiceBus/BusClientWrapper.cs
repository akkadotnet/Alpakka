using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace Akka.Streams.Azure.ServiceBus
{
    internal interface IBusClient
    {
        Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveBatchAsync(int messageCount, TimeSpan serverWaitTime);

        Task SendBatchAsync(IEnumerable<ServiceBusMessage> messages);
    }
    
    internal sealed class ServiceBusClientWrapper : IBusClient
    {
        private readonly ServiceBusSender _sender;
        private readonly ServiceBusReceiver _receiver;

        public ServiceBusClientWrapper(ServiceBusReceiver receiver)
        {
            _receiver = receiver;
        }

        public ServiceBusClientWrapper(ServiceBusSender sender)
        {
            _sender = sender;
        }

        public ServiceBusClientWrapper(ServiceBusSender sender, ServiceBusReceiver receiver)
        {
            _sender = sender;
            _receiver = receiver;
        }
        public ServiceBusReceiver Receiver { get; }

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveBatchAsync(int messageCount, TimeSpan serverWaitTime)
            => _receiver?.ReceiveMessagesAsync(messageCount, serverWaitTime) ?? throw new ArgumentException("No service bus receiver configured");

        public Task SendBatchAsync(IEnumerable<ServiceBusMessage> messages) => _sender?.SendMessagesAsync(messages) ?? throw new ArgumentException("No service bus sender configured");
    }

}