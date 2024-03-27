using System;
using System.Collections.Generic;

namespace Akka.Streams.SignalR
{
    /// <summary>
    /// A common static class for building messages returned to SignalR clients.
    /// </summary>
    public static class Signals
    {
        public static ISignalRResult Send(string connectionId, object data)
            => new Send(connectionId, data);

        public static ISignalRResult SendToGroup(string group, object data, IReadOnlyList<string> excluded = null)
            => new Send(group, data, excluded);

        public static ISignalRResult Broadcast(object data, IReadOnlyList<string> excluded = null)
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
        private static readonly string[] Empty = Array.Empty<string>();

        public string ConnectionId { get; }
        public string Group { get; }
        public IReadOnlyList<string> ExcludedConnectionIds { get; }
        public object Data { get; }

        public Send(string group, object data, IReadOnlyList<string> excludedConnectionIds)
        {
            Group = group;
            Data = data;
            ExcludedConnectionIds = excludedConnectionIds ?? Empty;
        }

        public Send(string connectionId, object data)
        {
            ConnectionId = connectionId;
            Data = data;
        }
    }

    public sealed class Broadcast : ISignalRResult
    {
        private static readonly string[] Empty = Array.Empty<string>();

        public object Data { get; }
        public IReadOnlyList<string> ExcludedConnectionIds { get; }

        public Broadcast(object data, IReadOnlyList<string> excludedConnectionIds = null)
        {
            Data = data;
            ExcludedConnectionIds = excludedConnectionIds ?? Empty;
        }
    }
}
