using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.Internals;
using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR
{
    public abstract class StreamConnection : PersistentConnection
    {
        #region lifecycle

        protected override Task OnConnected(IRequest request, string connectionId)
        {
            return base.OnConnected(request, connectionId);
        }

        protected override Task OnDisconnected(IRequest request, string connectionId, bool stopCalled)
        {
            return base.OnDisconnected(request, connectionId, stopCalled);
        }

        protected override Task OnReceived(IRequest request, string connectionId, string data)
        {
            return base.OnReceived(request, connectionId, data);
        }

        protected override Task OnReconnected(IRequest request, string connectionId)
        {
            return base.OnReconnected(request, connectionId);
        }

        protected override IList<string> OnRejoiningGroups(IRequest request, IList<string> groups, string connectionId)
        {
            return base.OnRejoiningGroups(request, groups, connectionId);
        }

        #endregion

        /// <summary>
        /// Creates a sink from the current <see cref="StreamConnection"/>. 
        /// Sinks can be used by Akka.Streams <see cref="Source{TOut,TMat}"/> 
        /// and <see cref="Flow{TIn,TOut,TMat}"/> to consume incoming messages 
        /// and push them down the SignalR web sockets.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="settings"></param>
        /// <returns></returns>
        public Sink<T, NotUsed> AsSink<T>(ConnectionSinkSettings<T> settings = null)
        {
            settings = settings ?? ConnectionSinkSettings<T>.Default;
            var sink = Sink.FromGraph(new SignalRConnectionStage<T>(this, Role.Sink))
                .AddAttributes(ActorAttributes.CreateDispatcher(settings.DispatcherId));
            return sink;
        }
    }
}
