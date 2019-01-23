using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;

namespace SignalRSample
{
    class Program
    {
        static void Main(string[] args)
        {
            Config config = @"
                akka.loglevel = DEBUG
            ";

            using (var system = ActorSystem.Create("web-system", config))
            using (var materializer = system.Materializer())
            using (var host = BuildWebHost(args))
            {
                App.System = system;
                App.Materializer = materializer;

                host.Run();
            }
        }

        public static IWebHost BuildWebHost(string[] args)
        {
            return WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .Build();
        }
    }
}
