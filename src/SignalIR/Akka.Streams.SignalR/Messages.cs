using System.Collections.Generic;
using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR
{
    /// <summary>
    /// A common static class for building messages returned to SignalR clients.
    /// </summary>
    public static class Signals
    {
        public static ISignalRResult Send(IList<string> signals, object data, IList<string> excluded = null)
            => new Send(signals, data, excluded);

        public static ISignalRResult Send(string signal, object data)
            => new Send(signal, data);

        public static ISignalRResult Broadcast(object data, string[] excluded = null)
            => new Broadcast(data, excluded);
    }

    /// <summary>
    /// Common interface for a messages returned to SignalR clients.
    /// </summary>
    public interface ISignalRResult
    {
        /// <summary>
        /// Payload sent to the client.
        /// </summary>
        object Data { get; }
    }

    internal sealed class Send : ISignalRResult
    {
        private static string[] empty = new string[0];

        public IList<string> Signals { get; }
        public IList<string> Excluded { get; }
        public object Data { get; }

        public Send(IList<string> signals, object data, IList<string> excluded = null)
        {
            Signals = signals;
            Excluded = excluded ?? empty;
            Data = data;
        }

        public Send(string signal, object data)
        {
            Signals = new[] { signal };
            Data = data;
        }

        public ConnectionMessage ToConnectionMessage()
        {
            return new ConnectionMessage(Signals, Data, Excluded);
        }
    }

    internal sealed class Broadcast : ISignalRResult
    {
        private static string[] empty = new string[0];

        public object Data { get; }
        public string[] Excluded { get; }

        public Broadcast(object data, string[] excluded = null)
        {
            Data = data;
            Excluded = excluded ?? empty;
        }
    }
}
