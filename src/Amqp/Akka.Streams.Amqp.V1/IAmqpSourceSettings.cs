using System.Threading.Tasks;
using Amqp;

namespace Akka.Streams.Amqp.V1
{
    public interface IAmqpSourceSettings<out T>
    {
        ReceiverLink GetReceiverLink();
        int Credit { get; }
        bool ManageConnection { get; }
        T Convert(Message message);

        void CloseConnection();
        Task CloseConnectionAsync();
    }
}
