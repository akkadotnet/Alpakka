using Microsoft.AspNetCore.SignalR;
using System;

namespace Akka.Streams.SignalR.AspNetCore
{
    /// <summary>
    /// A common interface for all events incoming from SignalR socket.
    /// 
    /// Available event types are:
    /// - <see cref="Received"/>
    /// - <see cref="Connected"/>
    /// - <see cref="Disconnected"/>
    /// </summary>
    public interface ISignalREvent
    {
        /// <summary>
        /// SignalR request attached to current event.
        /// </summary>
        HubCallerContext Request { get; }
    }

    /// <summary>
    /// A standard message send explicitly from the client with data attached.
    /// </summary>
    public sealed class Received : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public HubCallerContext Request { get; }

        /// <summary>
        /// Payload sent by the client.
        /// </summary>
        public object Data { get; }

        public Received(HubCallerContext request, object data)
        {
            Request = request;
            Data = data;
        }

        public override string ToString()
            => $"Received(connectionId: {Request.ConnectionId}, data: {Data}, request: {Request})";
    }

    /// <summary>
    /// An event send, when a new connection has been established.
    /// </summary>
    public sealed class Connected : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public HubCallerContext Request { get; }

        public Connected(HubCallerContext request)
        {
            Request = request;
        }

        public override string ToString()
            => $"Connected(connectionId: {Request.ConnectionId}, request: {Request})";
    }

    /// <summary>
    /// An event send, when an existing connection has been lost.
    /// </summary>
    public sealed class Disconnected : ISignalREvent
    {
        /// <inheritdoc cref="ISignalREvent"/>
        public HubCallerContext Request { get; }

        public Exception Exception { get; }

        public bool StopCalled => Exception == null;

        public Disconnected(HubCallerContext request, Exception error)
        {
            Request = request;
            Exception = error;
        }

        public override string ToString()
            => $"Disconnected(connectionId: {Request.ConnectionId}, stopCalled: {StopCalled}, request: {Request})";
    }
}