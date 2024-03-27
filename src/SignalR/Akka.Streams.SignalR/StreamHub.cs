using System;
using System.Threading.Tasks;
using Akka.Streams.SignalR.Internals;
using Microsoft.AspNetCore.SignalR;

namespace Akka.Streams.SignalR
{
    /// <summary>
    /// A variant of <see cref="Hub"/> able to cooperate with Akka.Streams API.
    /// Note this is instantiated by AspNetCore framework on EVERY method call - it should not carry 
    /// state.
    /// </summary>
    public abstract class StreamHub<TStream> : Hub<IClientSink>, IServerSource
        where TStream : StreamConnector
    {
        private readonly IStreamDispatcher _dispatcher;

        protected StreamHub(IStreamDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// Called by SignalR clients
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public Task Send(object data)
        {
            _dispatcher.Send<TStream>(new Received(Context, data), GetType());
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception ex)
        {
            _dispatcher.Send<TStream>(new Disconnected(Context, ex), GetType());
            return base.OnDisconnectedAsync(ex);
        }

        public override Task OnConnectedAsync()
        {
            _dispatcher.Send<TStream>(new Connected(Context), GetType());
            return base.OnConnectedAsync();
        }

    }

}
