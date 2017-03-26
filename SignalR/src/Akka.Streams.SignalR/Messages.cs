using System.Collections.Generic;
using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR
{
    public sealed class ReceivedMessage
    {
        public IRequest Request { get; }
        public string ConnectionId { get; }
        public string Data { get; }

        public ReceivedMessage(IRequest request, string connectionId, string data)
        {
            Request = request;
            ConnectionId = connectionId;
            Data = data;
        }
    }

    public sealed class Result
    {
        public IList<string> Signals { get; }
        public IList<string> Excluded { get; }
        public object Data { get; }

        public Result(IList<string> signals, object data, IList<string> excluded = null)
        {
            Signals = signals;
            Excluded = excluded;
            Data = data;
        }

        public Result(object data)
        {
            Data = data;
        }

        public ConnectionMessage ToConnectionMessage() => new ConnectionMessage(Signals, Data, Excluded);
    }
}
