using System.Threading.Tasks;

namespace Akka.Streams.SignalR
{
    public interface IServerSource
    {
        /// <summary>
        /// Called by SignalR client to send message to server
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Task Send(object data);
    }
}
