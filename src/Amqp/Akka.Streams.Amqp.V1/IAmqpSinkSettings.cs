using System.Threading.Tasks;
using Akka.IO;
using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public interface IAmqpSinkSettings<in T>
    {
        bool ManageConnection { get; }
        SenderLink GetSenderLink();
        byte[] GetBytes(T obj);

        void CloseConnection();
        Task CloseConnectionAsync();
    }
}
