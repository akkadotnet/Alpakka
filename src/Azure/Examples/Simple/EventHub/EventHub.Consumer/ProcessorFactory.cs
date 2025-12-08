using Akka.Streams.Azure.EventHub;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;

namespace EventHub.Consumer
{
    internal class ProcessorFactory : IProcessorFactory<EventProcessorClient>
    {
        public EventProcessorClient CreateProcessor()
        {
            var eventHubConnectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__eventhubs");
            if (eventHubConnectionString is null)
                throw new Exception("The environment variable CONNECTIONSTRINGS__eventhubs was not set.");

            var blobStorageConnectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__checkpoint");
            if (blobStorageConnectionString is null)
                throw new Exception("The environment variable CONNECTIONSTRINGS__checkpoint was not set.");

            var storageClient = new BlobContainerClient(blobStorageConnectionString, "checkpoint");
            storageClient.CreateIfNotExists();

            return new EventProcessorClient(storageClient, "orders-consumer", eventHubConnectionString, "orders");
        }
    }
}
