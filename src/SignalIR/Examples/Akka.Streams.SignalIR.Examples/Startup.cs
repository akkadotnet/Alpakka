using System;
using System.Threading;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.StaticFiles;
using Owin;
using SignalRSample;

[assembly: OwinStartup(typeof(Startup))]

namespace SignalRSample
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseFileServer(new FileServerOptions
            {
                EnableDirectoryBrowsing = true
            });
            app.MapSignalR<EchoConnection>("/echo");
        }
    }
}
