using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Microsoft.ServiceBus.Messaging;

namespace Akka.Streams.Azure.ServiceBus.Examples
{
    class Program
    {
        private const string ConnectionString = "{ServiceBus connection string}";
        private const string QueueName = "{ServiceBus queue name}";

        static void Main()
        {
            var client = QueueClient.CreateFromConnectionString(ConnectionString, QueueName);

            using (var sys = ActorSystem.Create("ServiceBusSystem"))
            {
                using (var mat = sys.Materializer())
                {
                    Console.WriteLine("Writing messages into the queue");
                    var t = Source.From(Enumerable.Range(1,100))
                        .Select(x => new BrokeredMessage("Message: " + x))
                        .Grouped(10)
                        .ToServiceBus(client, mat);
                    t.Wait();
                    Console.WriteLine("Finished ");


                    Console.WriteLine("Reading messages from the queue");
                    t = ServiceBusSource.Create<string>(client).RunForeach(Console.WriteLine, mat);
                  
                    Console.WriteLine("Press any key to exit");
                    Console.ReadKey();
                }
            }
        }
    }
}
