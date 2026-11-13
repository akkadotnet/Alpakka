using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace SignalRSample;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        using var system = ActorSystem.Create("web-system", "akka.loglevel = DEBUG");
        using var materializer = system.Materializer();
        App.System = system;
        App.Materializer = materializer;
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(web => web.UseStartup<Startup>()).Build();
        try { await host.RunAsync(); }
        finally { await system.Terminate(); }
    }
}
