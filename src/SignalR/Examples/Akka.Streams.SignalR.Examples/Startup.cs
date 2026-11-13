using Akka.Streams;
using Akka.Streams.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SignalRSample;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new ConnectionSourceSettings(102400, OverflowStrategy.DropBuffer))
            .AddSignalRAkkaStream().AddSignalR();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseDefaultFiles().UseStaticFiles().UseRouting().UseEndpoints(routes =>
        {
            routes.MapHub<EchoHub>("/echo");
            routes.MapGet("/health", () => "ready");
        });
    }
}
