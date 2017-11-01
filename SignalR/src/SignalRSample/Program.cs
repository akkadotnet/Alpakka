using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Configuration;
using Microsoft.Owin.Hosting;

namespace SignalRSample
{
    class Program
    {
        static void Main(string[] args)
        {
            var url = "http://localhost:5000";

            Config config = @"
                akka.loglevel = DEBUG
            ";

            using (var system = ActorSystem.Create("web-system", config))
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
