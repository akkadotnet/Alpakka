using System;

namespace Akka.Streams.SignalR.AspNetCore.Internals
{
    public interface IStreamDispatcher
    {
        /// <summary>
        /// Send message to the appropriate stream connector for a hub type
        /// </summary>
        /// <typeparam name="TStream"></typeparam>
        /// <param name="message"></param>
        /// <param name="hubType"></param>
        void Send<TStream>(ISignalREvent message, Type hubType) 
            where TStream : StreamConnector;
    }
}