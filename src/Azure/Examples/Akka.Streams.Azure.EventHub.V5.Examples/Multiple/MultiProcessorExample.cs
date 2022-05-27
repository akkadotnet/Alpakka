//-----------------------------------------------------------------------
// <copyright file="MultiProcessorExample.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

namespace Akka.Streams.Azure.EventHub.Examples.Multiple
{
    public static class MultiProcessorExample
    {
        // Number of processing actors that will be created to service the EventHub consumer group.
        // NOTE: Should never exceed the number of partition of the EventHub
        private const int WorkerCount = 2;
        
        public static async Task Run(bool batched)
        {
            using var sys = ActorSystem.Create("EventHubSystem", ConfigurationFactory.ParseString("akka.loglevel = DEBUG"));
            
            var eventHubActor = sys.ActorOf(EventHubProcessorActor.Props(WorkerCount, batched));
            
            eventHubActor.Tell(Start.Instance);
            
            Console.WriteLine("Press enter key to send some messages into the EventHub.");
            Console.ReadLine();

            var killSwitch = KillSwitches.Shared("producer-kill-switch");
            var client = new EventHubProducerClient(ProcessorFactory.EventHubConnectionString, ProcessorFactory.EventHubName);
            var task = Source.From(Enumerable.Range(1, int.MaxValue))
                .Select(i => new EventData(Encoding.UTF8.GetBytes("Message from EventHubSink : " + i)))
                .Grouped(10)
                .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                .ViaMaterialized(killSwitch.Flow<IEnumerable<EventData>>(), Keep.Both)
                .ToEventHub(client, sys.Materializer());
            
            Console.WriteLine("Receiving. Press enter key to stop worker.");
            Console.ReadLine();

            killSwitch.Shutdown();
            await task;
            await eventHubActor.Ask<Stopped>(Stop.Instance);
            
            Console.WriteLine("Press enter key to exit.");
            Console.ReadLine();
            
        }
    }
}