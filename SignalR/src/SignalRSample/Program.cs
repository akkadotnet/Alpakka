using System;
using Akka.Actor;
using Akka.Streams;
using Microsoft.Owin.Hosting;

namespace SignalRSample
{
    class Program
    {
        static void Main(string[] args)
        {
            var url = "http://localhost:5000";

            using (var system = ActorSystem.Create("web-system"))
            using (var materializer = system.Materializer())
            using (WebApp.Start<Startup>(url))
            {
                App.System = system;
                App.Materializer = materializer;

                Console.WriteLine($"Listening at {url}");
                Console.ReadLine();
            }
        }
    }
}
