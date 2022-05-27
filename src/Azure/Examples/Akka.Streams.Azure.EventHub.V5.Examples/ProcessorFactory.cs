//-----------------------------------------------------------------------
// <copyright file="ProcessorFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;

namespace Akka.Streams.Azure.EventHub.Examples
{
    public static class ProcessorFactory
    {
        public const string EventHubConnectionString = "{Event Hub connection string}";
        public const string EventHubName = "{Event Hub name}";
        private const string ConsumerGroupName = "$Default"; // Default consumer group name
        
        private const string StorageAccountName = "{storage account name}";
        private const string StorageAccountKey = "{storage account key}";
        private static readonly string StorageConnectionString = $"DefaultEndpointsProtocol=https;AccountName={StorageAccountName};AccountKey={StorageAccountKey}";
        private const string BlobContainerName = "{blob container name}";

        public sealed class EventProcessorFactory : IProcessorFactory<EventProcessorClient>
        {
            public EventProcessorClient CreateProcessor()
            {
                var storageClient  = new BlobContainerClient(StorageConnectionString, BlobContainerName);
                var options = new EventProcessorClientOptions
                    {
                        MaximumWaitTime = TimeSpan.FromSeconds(15),
                        RetryOptions = { TryTimeout = TimeSpan.FromSeconds(15) }
                    };
                return new EventProcessorClient(storageClient, ConsumerGroupName, EventHubConnectionString, EventHubName, options);
            }
        }
        
        public class BatchedEventProcessorFactory : IProcessorFactory<BatchedEventProcessorClient>
        {
            public BatchedEventProcessorClient CreateProcessor()
            {
                var storageClient  = new BlobContainerClient(StorageConnectionString, BlobContainerName);
                var options = new EventProcessorClientOptions
                {
                    MaximumWaitTime = TimeSpan.FromSeconds(15),
                    RetryOptions = { TryTimeout = TimeSpan.FromSeconds(15) }
                };
                return new BatchedEventProcessorClient(storageClient, ConsumerGroupName, EventHubConnectionString, EventHubName, options);
            }
        }
    }
}