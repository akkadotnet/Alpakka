using System;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Azure.StorageQueue;
using Akka.Streams.Dsl;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

internal static class Program
{
    private static async Task Main()
    {
        var queue = new QueueClient("UseDevelopmentStorage=true", "compatibility");
        var receiveOptions = new GetRequestOptions(TimeSpan.FromMinutes(1));
        var sendOptions = new AddRequestOptions(TimeSpan.FromDays(1), TimeSpan.FromSeconds(1));
        Source<QueueMessage, NotUsed> source = QueueSource.Create(queue, 1, TimeSpan.FromSeconds(1), receiveOptions);
        Sink<string, Task> sink = QueueSink.Create(queue, sendOptions);
        var sourceStage = new QueueSource(queue, 1, TimeSpan.FromSeconds(1), receiveOptions);
        var sinkStage = new QueueSink(queue, sendOptions);
        if (source is null || sink is null || sourceStage.Out is null || sinkStage.In is null)
            throw new InvalidOperationException("A retained API did not produce a stream stage.");

        var system = ActorSystem.Create("old-storage-queue-consumer");
        try
        {
            await Source.Empty<string>().ToStorageQueue(queue, system.Materializer());
        }
        finally
        {
            await system.Terminate();
        }

        Console.WriteLine("Previously compiled Storage Queue consumer passed.");
    }
}
