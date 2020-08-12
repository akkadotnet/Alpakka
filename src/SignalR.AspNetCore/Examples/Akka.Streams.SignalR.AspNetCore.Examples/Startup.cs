using Akka.Streams;
using Akka.Streams.SignalR.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SignalRSample
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton(new ConnectionSourceSettings(102400, OverflowStrategy.DropBuffer))
                .AddSignalRAkkaStream()
                .AddSignalR();
        }

        public void Configure(IApplicationBuilder app)
        {
            app
                .UseStaticFiles()
                .UseRouting()
                .UseEndpoints(routes =>
                {
                    routes.MapHub<EchoHub>("/echo");
                });
        }
    }
}
