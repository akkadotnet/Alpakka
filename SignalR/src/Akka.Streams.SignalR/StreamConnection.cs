using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.SignalR.Internals;
using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR
{
    public abstract class StreamConnection : PersistentConnection
    {
        public event EventHandler<ReceivedMessage> Received;
        
        protected StreamConnection(ConnectionSourceSettings sourceSettings = null, ConnectionSinkSettings sinkSettings = null)
        {
            this.Sink = Dsl.Sink.FromGraph(new SignalRSinkStage(this, sinkSettings ?? ConnectionSinkSettings.Default));
            this.Source = Dsl.Source.FromGraph(new SignalRSourceStage(this, sourceSettings ?? ConnectionSourceSettings.Default));
        }

        public Sink<ISignalRResult, NotUsed> Sink { get; }
        public Source<ReceivedMessage, NotUsed> Source { get; }
        
        #region lifecycle
        
        protected override Task OnReceived(IRequest request, string connectionId, string data)
        {
            this.Received?.Invoke(this, new ReceivedMessage(request, connectionId, data));
            return base.OnReceived(request, connectionId, data);
        }

        #endregion

    }
}
