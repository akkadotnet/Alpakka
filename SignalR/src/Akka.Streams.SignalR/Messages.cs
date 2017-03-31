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

        public override string ToString()
            => $"ReceivedMessage(connectionId: {ConnectionId}, data: {Data}, request: {Request})";
    }

    public static class Signals
    {
        public static ISignalRResult Send(IList<string> signals, object data, IList<string> excluded = null)
            => new SendSignal(signals, data, excluded);

        public static ISignalRResult Send(string signal, object data)
            => new SendSignal(signal, data);

        public static ISignalRResult Broadcast(object data, string[] excluded = null)
            => new BroadcastSignal(data, excluded);

    }

    public interface ISignalRResult { }

    internal sealed class SendSignal : ISignalRResult
    {
        private static string[] empty = new string[0];

        public IList<string> Signals { get; }
        public IList<string> Excluded { get; }
        public object Data { get; }

        public SendSignal(IList<string> signals, object data, IList<string> excluded = null)
        {
            Signals = signals;
            Excluded = excluded ?? empty;
            Data = data;
        }

        public SendSignal(string signal, object data)
        {
            Signals = new[] { signal };
            Data = data;
        }

        public ConnectionMessage ToConnectionMessage()
        {
            return new ConnectionMessage(Signals, Data, Excluded);
        }
    }

    internal sealed class BroadcastSignal : ISignalRResult
    {
        private static string[] empty = new string[0];

        public object Data { get; }
        public string[] Excluded { get; }

        public BroadcastSignal(object data, string[] excluded = null)
        {
            Data = data;
            Excluded = excluded ?? empty;
        }
    }
}
