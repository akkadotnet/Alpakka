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
        private readonly IBuffer<ReceivedMessage> buffer;

        protected StreamConnection()
        {
            this.buffer = new FixedSizeBuffer<ReceivedMessage>(128);
            this.Sink = Dsl.Sink.FromGraph(new SignalRSinkStage(this));
            this.Source = Dsl.Source.FromGraph(new SignalRSourceStage(this, buffer));
        }

        public Sink<Result, NotUsed> Sink { get; }
        public Source<ReceivedMessage, NotUsed> Source { get; }
        
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
            buffer.TryEnqueue(new ReceivedMessage(request, connectionId, data));
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

    }
}
