using Amqp;
using Amqp.Framing;
using Amqp.Types;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Streams.Amqp.V1.Tests
{
    public class SimpleAmqpTest
    {
        [Fact]
        public async Task TestHelloWorld()
        {
            //strange, works using regular activeMQ and the amqp test broker from here: http://azure.github.io/amqpnetlite/articles/hello_amqp.html
            //but this does not work in ActiveMQ Artemis
            Address address = new Address("amqp://guest:guest@localhost:5672");
            Connection connection = await Connection.Factory.CreateAsync(address);
            Session session = new Session(connection);
            
            Message message = new Message("Hello AMQP");

            Target target = new Target
            {
                Address = "q1",
                Capabilities = new Symbol[] { new Symbol("queue") }
            };

            SenderLink sender = new SenderLink(session, "sender-link", target, null);
            await sender.SendAsync(message);

            Source source = new Source
            {
                Address = "q1",
                Capabilities = new Symbol[] { new Symbol("queue") }
            };

            ReceiverLink receiver = new ReceiverLink(session, "receiver-link", source, null);
            message = await receiver.ReceiveAsync();
            receiver.Accept(message);

            await sender.CloseAsync();
            await receiver.CloseAsync();
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }
}
