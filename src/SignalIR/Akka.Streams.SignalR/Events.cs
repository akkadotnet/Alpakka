using Microsoft.AspNet.SignalR;

namespace Akka.Streams.SignalR
{
    /// <summary>
    /// A common interface for all events incoming from SignalR socket.
    /// 
    /// Available event types are:
    /// - <see cref="Received"/>
    /// - <see cref="Connected"/>
    /// - <see cref="Disconnected"/>
    /// - <see cref="Reconnected"/>
    /// </summary>
    public interface ISignalREvent
    {
        /// <summary>
        /// SignalR request attached to current event.
        /// </summary>
        IRequest Request { get; }

        /// <summary>
        /// Identifier of a connection, which has sent the event.
        /// </summary>
        string ConnectionId { get; }
    }

    /// <summary>
    /// A standard message send explicitly from the client with data attached.
    /// </summary>
    public sealed class Received : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public IRequest Request { get; }

        /// <inheritdoc cref="ISignalREvent"/>
        public string ConnectionId { get; }

        /// <summary>
        /// Payload send by the client.
        /// </summary>
        public string Data { get; }

        public Received(IRequest request, string connectionId, string data)
        {
            Request = request;
            ConnectionId = connectionId;
            Data = data;
        }

        public override string ToString()
            => $"Received(connectionId: {ConnectionId}, data: {Data}, request: {Request})";
    }

    /// <summary>
    /// An event send, when a new connection has been established.
    /// </summary>
    public sealed class Connected : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public IRequest Request { get; }

        /// <inheritdoc cref="ISignalREvent"/>
        public string ConnectionId { get; }

        public Connected(IRequest request, string connectionId)
        {
            Request = request;
            ConnectionId = connectionId;
        }

        public override string ToString()
            => $"Connected(connectionId: {ConnectionId}, request: {Request})";
    }

    /// <summary>
    /// An event send, when an existing connection has been lost.
    /// </summary>
    public sealed class Disconnected : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public IRequest Request { get; }

        /// <inheritdoc cref="ISignalREvent"/>
        public string ConnectionId { get; }

        public bool StopCalled { get; }

        public Disconnected(IRequest request, string connectionId, bool stopCalled)
        {
            Request = request;
            ConnectionId = connectionId;
            StopCalled = stopCalled;
        }

        public override string ToString()
            => $"Disconnected(connectionId: {ConnectionId}, stopCalled: {StopCalled}, request: {Request})";
    }

    /// <summary>
    /// An event send, when disconnected client has been reconnected again.
    /// </summary>
    public sealed class Reconnected : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public IRequest Request { get; }

        /// <inheritdoc cref="ISignalREvent"/>
        public string ConnectionId { get; }

        public Reconnected(IRequest request, string connectionId)
        {
            Request = request;
            ConnectionId = connectionId;
        }

        public override string ToString()
            => $"Reconnected(connectionId: {ConnectionId},  request: {Request})";
    }
}