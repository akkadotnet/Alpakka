using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR;
using Microsoft.AspNet.SignalR;

namespace SignalRSample
{
    public class EchoConnection : StreamConnection
    {
        private readonly ICancelable cancel;

        public EchoConnection()
        {
            var t = TimeSpan.FromSeconds(5);
            this.cancel = Akka.Streams.Dsl.Source.Tick(t, t, "hello")
                .Log("signalr")
                .Select(msg => new Result(msg))
                .To(this.Sink)
                .Run(App.Materializer);
        }

        protected override Task OnDisconnected(IRequest request, string connectionId, bool stopCalled)
        {
            cancel.Cancel();
            return base.OnDisconnected(request, connectionId, stopCalled);
        }
    }
}