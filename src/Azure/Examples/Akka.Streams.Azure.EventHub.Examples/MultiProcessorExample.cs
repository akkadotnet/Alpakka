using System;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Streams.Dsl;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;

namespace Akka.Streams.Azure.EventHub.Examples
{
    public static class MultiProcessorExample
    {
        private const string EventHubConnectionString = "{Event Hub connection string}";
        private const string EventHubName = "{Event Hub name}";
        private const string EventHubConsumerGroup = "$default"; // default EventHub group name
        private const string StorageAccountName = "{storage account name}";
        private const string StorageAccountKey = "{storage account key}";
        private static readonly string StorageConnectionString = $"DefaultEndpointsProtocol=https;AccountName={StorageAccountName};AccountKey={StorageAccountKey}";

        public static void Run()
        {
            using var sys = ActorSystem.Create("EventHubSystem");
            using var mat = sys.Materializer();

            var runnableGraph = Source.FromGraph(new EventHubSource())
                .Select(t =>
                {
                    var (partitionContext, eventData) = t;
                    Console.WriteLine("Message from Partition: " + partitionContext.Lease.PartitionId);
                    return eventData;
                })
                .Select(e => Encoding.UTF8.GetString(e.Body))
                .ToMaterialized(Sink.ForEach((string s) => Console.WriteLine(s)), Keep.Left);

            var eventProcessorHostName = Guid.NewGuid().ToString();
            var eventProcessorHost = new EventProcessorHost(eventProcessorHostName, EventHubName, EventHubConsumerGroup, EventHubConnectionString, StorageConnectionString);
            Console.WriteLine("Registering EventProcessor...");
            var options = new EventProcessorOptions();
            options.SetExceptionHandler(args =>
            {
                Console.WriteLine($"[Host: {args.Hostname}, Partition: {args.PartitionId}, Action: {args.Action}]: {args.Exception.Message}\n{args.Exception.StackTrace}");
            });
            eventProcessorHost.RegisterEventProcessorFactoryAsync(new MultiProcessorFactory(runnableGraph, mat));

            Console.WriteLine("Processor registered...");
            Console.WriteLine("Press enter key to send some messages into the EventHub.");
            Console.ReadLine();

            var client = EventHubClient.CreateFromConnectionString(EventHubConnectionString);
            Source.From(Enumerable.Range(1, 100))
                .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
                .Grouped(10)
                .ToEventHub(client, mat);

            Console.WriteLine("Receiving. Press enter key to stop worker.");
            Console.ReadLine();
            eventProcessorHost.UnregisterEventProcessorAsync().Wait();
        }
    }
}
