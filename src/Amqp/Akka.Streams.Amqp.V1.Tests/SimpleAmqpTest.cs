using System;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Amqp.Tests;
using Xunit;
using Xunit.Abstractions;
using Address = Amqp.Address;

namespace Akka.Streams.Amqp.V1.Tests
{
    [Collection("AmqpSpec")]
    public class SimpleAmqpV1Test
    {
        private readonly AmqpFixture _fixture;

        public SimpleAmqpV1Test(AmqpFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task TestHelloWorld()
        {
            var address = new Address(_fixture.HostName, _fixture.AmqpPort, _fixture.UserName, _fixture.Password, scheme: "AMQP");
            var connection = new Connection(address);
            var session = new Session(connection);
            
            var message = new Message("Hello AMQP");

            var target = new Target
            {
                Address = "q1",
                Capabilities = new Symbol[] { new Symbol("queue") }
            };

            var sender = new SenderLink(session, "sender-link", target, null);
            await sender.SendAsync(message);

            var source = new Source
            {
                Address = "q1",
                Capabilities = new Symbol[] { new Symbol("queue") }
            };

            var receiver = new ReceiverLink(session, "receiver-link", source, null);
            message = await receiver.ReceiveAsync();
            receiver.Accept(message);

            await sender.CloseAsync();
            await receiver.CloseAsync();
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }
}
