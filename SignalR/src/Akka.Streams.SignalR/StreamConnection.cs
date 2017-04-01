using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.Internals;
using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR
{
    /// <summary>
    /// A varian of <see cref="PersistentConnection"/> able to cooperate with Akka.Streams API.
    /// </summary>
    public abstract class StreamConnection : PersistentConnection
    {
        internal event EventHandler<ISignalREvent> Events;
        
        /// <summary>
        /// Creates a new instance of <see cref="StreamConnection"/>.
        /// </summary>
        /// <param name="sourceSettings">Optional settings used to configure the <see cref="Source"/>.</param>
        /// <param name="sinkSettings">Optional settings used to configure the <see cref="Sink"/>.</param>
        protected StreamConnection(ConnectionSourceSettings sourceSettings = null, ConnectionSinkSettings sinkSettings = null)
        {
            this.Sink = Dsl.Sink.FromGraph(new SignalRSinkStage(this, sinkSettings ?? ConnectionSinkSettings.Default));
            this.Source = Dsl.Source.FromGraph(new SignalRSourceStage(this, sourceSettings ?? ConnectionSourceSettings.Default));
        }

        /// <summary>
        /// An Akka.Streams sink for messages send back to the client.
        /// Can be integrated with Akka.Streams flow graphs.
        /// </summary>
        public Sink<ISignalRResult, NotUsed> Sink { get; }

        /// <summary>
        /// An Akka.Streams source used for events related with the connection.
        /// Can be integrated with Akka.Streams flow graphs.
        /// 
        /// Keep in mind, that in current version SignalR doesn't support backressure 
        /// mechanism. Incoming events, that cannot be processed in time, will be 
        /// buffered up to the  <see cref="ConnectionSourceSettings.BufferCapacity"/> 
        /// size, and once buffer will overflow, an 
        /// <see cref="ConnectionSourceSettings.OverflowStrategy"/>  will be applied.
        /// </summary>
        public Source<ISignalREvent, NotUsed> Source { get; }
        
        #region lifecycle
        
        protected override Task OnReceived(IRequest request, string connectionId, string data)
        {
            this.Events?.Invoke(this, new Received(request, connectionId, data));
            return base.OnReceived(request, connectionId, data);
        }

        protected override Task OnConnected(IRequest request, string connectionId)
        {
            this.Events?.Invoke(this, new Connected(request, connectionId));
            return base.OnConnected(request, connectionId);
        }

        protected override Task OnDisconnected(IRequest request, string connectionId, bool stopCalled)
        {
            this.Events?.Invoke(this, new Disconnected(request, connectionId, stopCalled));
            return base.OnDisconnected(request, connectionId, stopCalled);
        }

        protected override Task OnReconnected(IRequest request, string connectionId)
        {
            this.Events?.Invoke(this, new Reconnected(request, connectionId));
            return base.OnReconnected(request, connectionId);
        }
        
        #endregion

    }
}
