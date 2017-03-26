using Microsoft.AspNet.SignalR;
using Owin;

namespace Akka.Streams.SignalR.Tests.Internals
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            //app.MapSignalR<TestConnection>("/test");
        }
    }
}