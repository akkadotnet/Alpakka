// //-----------------------------------------------------------------------
// // <copyright file="SingleProcessorExample.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

namespace Akka.Streams.Azure.EventHub.V5.Examples.Single
{
    public static class SingleProcessorExample
    {
        private static readonly ConcurrentDictionary<string, int> PartitionEventCount = new ConcurrentDictionary<string, int>();
        
        public static async Task Run()
        {
            using var sys = ActorSystem.Create("EventHubSystem");
            using var mat = sys.Materializer();

            var factory = new ProcessorFactory.EventProcessorFactory();
            var processor = Source.FromGraph(new EventHubSource(factory))
                .SelectAsync(1, async t =>
                {
                    Console.WriteLine($"Message from Partition: {t.Partition.PartitionId}");
                    await Business(t);
                    return Done.Instance;
                })
                .ToMaterialized(Sink.Ignore<Done>(), Keep.Left)
                .Run(mat);
            
            Console.WriteLine("Press enter key to send some messages into the EventHub.");
            Console.ReadLine();

            var killSwitch = KillSwitches.Shared("producer-kill-switch");
            var client = new EventHubProducerClient(ProcessorFactory.EventHubConnectionString, ProcessorFactory.EventHubName);
            var task = Source.From(Enumerable.Range(1, int.MaxValue))
                .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
                .Grouped(10)
                .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                .ViaMaterialized(killSwitch.Flow<IEnumerable<EventData>>(), Keep.Both)
                .ToEventHub(client, mat);
            
            Console.WriteLine("Receiving. Press enter key to stop worker.");
            Console.ReadLine();

            killSwitch.Shutdown();
            await task;
            await processor.StopProcessingAsync();
            
            Console.WriteLine("Press enter key to exit.");
            Console.ReadLine();
        }

        private static async Task Business(EventHubSource.ProcessContext context)
        {
            var body = Encoding.UTF8.GetString(context.Event.Body.Span);
            Console.WriteLine($"Processing message: {body}");
            
            var totalEvents = PartitionEventCount.AddOrUpdate(
                key: context.Partition.PartitionId,
                addValue: 1,
                updateValueFactory: (_, currentCount) => currentCount + 1);

            if (totalEvents % 50 == 0)
            {
                Console.WriteLine($"Updating checkpoint for partition [{context.Partition}]. Total events: [{totalEvents}]");
                await context.UpdateCheckpointAsync();
            }
        }
    }
}
