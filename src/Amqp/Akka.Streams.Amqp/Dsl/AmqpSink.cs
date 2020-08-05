using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.Dsl
{
    public static class AmqpSink
    {
        /// <summary>
        /// Creates an <see cref="AmqpSink"/> that accepts <see cref="OutgoingMessage"/> elements.
        /// </summary>
        /// <param name="settings">The sink settings</param>
        /// <returns>an <see cref="AmqpSink"/> that accepts <see cref="OutgoingMessage"/> elements.</returns>
        public static Sink<OutgoingMessage, Task> Create(AmqpSinkSettings settings)
        {
            return Sink.FromGraph(new AmqpSinkStage(settings));
        }
        
        /// <summary>
        /// Creates an <see cref="AmqpSink"/> that accepts <see cref="ByteString"/> elements.
        /// </summary>
        /// <param name="settings">the sink settings</param>
        /// <returns>an <see cref="AmqpSink"/> that accepts <see cref="ByteString"/> elements.</returns>
        public static Sink<ByteString, Task> CreateSimple(AmqpSinkSettings settings)
        {
            return Create(settings).ContraMap<ByteString>(bytes => new OutgoingMessage(bytes, false, false));
        }
        
        /// <summary>
        /// Connects to an AMQP server upon materialization and sends incoming messages to the server.
        /// Each materialized sink will create one connection to the broker. This stage sends messages to
        /// the queue named in the replyTo options of the message instead of from settings declared at construction.
        /// This stage materializes to a `Future[Done]`, which can be used to know when the Sink completes, either normally
        /// or because of an amqp failure
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static Sink<OutgoingMessage, Task> ReplyTo(AmqpReplyToSinkSettings settings)
        {
            return Sink.FromGraph(new AmqpReplyToSinkStage(settings));
        }
    }
}
