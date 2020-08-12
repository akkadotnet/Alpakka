using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.AspNetCore.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.Streams.SignalR.AspNetCore
{
    /// <summary>
    /// Inherit to connect processing stages to Source to receive incoming SignalR client messages, 
    /// and connect processing stages to Sink to send outgoing messages to SignalR clients.
    /// </summary>
    public abstract class StreamConnector
    {
        /// <summary>
        /// Creates a new instance of <see cref="StreamConnector"/>.
        /// </summary>
        /// <param name="clients"></param>
        /// <param name="sourceSettings">Optional settings used to configure the <see cref="Source"/>.</param>
        /// <param name="sinkSettings">Optional settings used to configure the <see cref="Sink"/>.</param>
        protected StreamConnector(IHubClients clients, ConnectionSourceSettings sourceSettings = null, ConnectionSinkSettings sinkSettings = null)
        {
            Sink = Dsl.Sink.FromGraph(new SignalRSinkStage(clients, sinkSettings ?? ConnectionSinkSettings.Default));
            Source = Dsl.Source.FromGraph(new SignalRSourceStage(this, sourceSettings ?? ConnectionSourceSettings.Default));
        }

        /// <summary>
        /// An Akka.Streams sink for messages send back to the client.
        /// Can be integrated with Akka.Streams flow graphs.
        /// </summary>
        public Sink<ISignalRResult, NotUsed> Sink { get; private set; }

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
        public Source<ISignalREvent, NotUsed> Source { get; private set; }

        /// <summary>
        /// Listened to by Source stage to send events downstream
        /// </summary>
        internal event EventHandler<ISignalREvent> Events;

        /// <summary>
        /// Called by SignalR Hub to push client messages downstream via Events
        /// </summary>
        /// <param name="e"></param>
        internal void OnEvents(ISignalREvent e) => Events?.Invoke(this, e);
    }
}
