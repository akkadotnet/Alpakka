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
            services.AddSignalR();
            services.AddSignalRAkkaStream();
            services.Add(new ServiceDescriptor(typeof(ConnectionSourceSettings), 
                new ConnectionSourceSettings(102400, OverflowStrategy.DropBuffer)                
            ));
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseStaticFiles();
            app.UseSignalR(routes => {
                routes.MapHub<EchoHub>("/echo");
            });
        }
    }
}
