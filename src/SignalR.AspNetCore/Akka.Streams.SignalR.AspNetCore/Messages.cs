using System.Collections.Generic;

namespace Akka.Streams.SignalR.AspNetCore
{
    /// <summary>
    /// A common static class for building messages returned to SignalR clients.
    /// </summary>
    public static class Signals
    {
        public static ISignalRResult Send(string connectionId, object data)
            => new Send(connectionId, data);

        public static ISignalRResult SendToGroup(string group, object data, IList<string> excluded = null)
            => new Send(group, data, excluded);

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

    public sealed class Send : ISignalRResult
    {
        private static string[] empty = new string[0];

        public string ConnectionId { get; }
        public string Group { get; }
        public IList<string> ExcludedConnectionIds { get; }
        public object Data { get; }

        public Send(string group, object data, IList<string> excludedConnectionIds)
        {
            Group = group;
            Data = data;
            ExcludedConnectionIds = excludedConnectionIds ?? empty;
        }

        public Send(string connectionId, object data)
        {
            ConnectionId = connectionId;
            Data = data;
        }
    }

    public sealed class Broadcast : ISignalRResult
    {
        private static string[] empty = new string[0];

        public object Data { get; }
        public string[] ExcludedConnectionIds { get; }

        public Broadcast(object data, string[] excludedConnectionIds = null)
        {
            Data = data;
            ExcludedConnectionIds = excludedConnectionIds ?? empty;
        }
    }
}
