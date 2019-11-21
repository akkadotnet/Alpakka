using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Streams.SignalR.AspNetCore
{
    public interface IClientSink
    {
        /// <summary>
        /// Called by server to send message to connected client
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Task Receive(object data);
    }
}
