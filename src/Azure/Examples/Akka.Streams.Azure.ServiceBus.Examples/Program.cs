using System;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Streams.Dsl;
using Azure.Messaging.ServiceBus;

namespace Akka.Streams.Azure.ServiceBus.Examples
{
    class Program
    {
        private const string ConnectionString = "[Azure service bus connection string]";
        private const string QueueOrTopicName = "[Azure service bus queue or topic name]";
        private const string SubscriptionName = "[Azure service bus subscription name]";

        static void Main()
        {
            var client = new ServiceBusClient(ConnectionString);
            var receiveClient = client.CreateReceiver(QueueOrTopicName);
            // To use a subscription:
            // var receiveClient = client.CreateReceiver(QueueOrTopicName, SubscriptionName);
            var senderClient = client.CreateSender(QueueOrTopicName);

            using var sys = ActorSystem.Create("ServiceBusSystem");
            using var mat = sys.Materializer();

            Console.WriteLine("Writing messages into the queue");
            var t = Source.From(Enumerable.Range(1,100))
                .Select(x => new ServiceBusMessage("Sending Message: " + x))
                .Grouped(10)
                .ToServiceBus(senderClient, mat);
            t.Wait();
            Console.WriteLine("Finished ");

            Console.WriteLine("Reading messages from the queue");
            t = ServiceBusSource.Create<string>(receiveClient, msg => Encoding.UTF8.GetString(msg.Body.ToArray())).RunForeach(x => Console.WriteLine("Received {0}", x), mat);
                  
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }
    }
}
