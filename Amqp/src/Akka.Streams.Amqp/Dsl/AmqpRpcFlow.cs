using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.Dsl
{
    public static class AmqpRpcFlow
    {
        /// <summary>
        /// The `committableFlow` makes it possible to commit (ack/nack) messages to RabbitMQ.
        /// This is useful when "at-least once delivery" is desired, as each message will likely be
        /// delivered one time but in failure cases could be duplicated.
        /// If you commit the offset before processing the message you get "at-most once delivery" semantics,
        /// and for that there is a <see cref="AtMostOnceFlow"/>.
        /// Compared to auto-commit, this gives exact control over when a message is considered consumed.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="bufferSize"></param>
        /// <param name="repliesPerMessage">The number of responses that should be expected for each message placed on the queue. This
        /// can be overridden per message by including <code>expectedReplies</code> in the the header of the <see cref="OutgoingMessage"/></param>
        /// <returns>TBD</returns>
        public static Flow<OutgoingMessage, CommittableIncomingMessage, Task<string>> CommittableFlow(AmqpSinkSettings settings,
            int bufferSize, int repliesPerMessage = 1)
        {
            return Flow.FromGraph(new AmqpRpcFlowStage(settings, bufferSize, repliesPerMessage));
        }

        /// <summary>
        /// Convenience for "at-most once delivery" semantics. Each message is acked to RabbitMQ before it is emitted downstream.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="bufferSize"></param>
        /// <param name="repliesPerMessage">The number of responses that should be expected for each message placed on the queue. This
        /// can be overridden per message by including <code>expectedReplies</code> in the the header of the <see cref="OutgoingMessage"/></param>
        /// <returns>TBD</returns>
        public static Flow<OutgoingMessage, IncomingMessage, Task<string>> AtMostOnceFlow(AmqpSinkSettings settings, 
            int bufferSize, int repliesPerMessage = 1)
        {
            return CommittableFlow(settings, bufferSize, repliesPerMessage)
                .SelectAsync(1, async cm =>
                {
                    await cm.Ack();
                    return cm.Message;
                });
        }

        /// <summary>
        /// Create an [[https://www.rabbitmq.com/tutorials/tutorial-six-java.html RPC style flow]] for processing and communicating
        /// over a rabbitmq message bus. This will create a private queue, and add the reply-to header to messages sent out.
        /// 
        /// This stage materializes to a <see cref="Task{String}"/>, which is the name of the private exclusive queue used for RPC communication.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="repliesPerMessage">The number of responses that should be expected for each message placed on the queue. This
        /// can be overridden per message by including <code>expectedReplies</code> in the the header of the <see cref="OutgoingMessage"/></param>
        /// <returns>TBD</returns>
        public static Flow<ByteString, ByteString, Task<string>> CreateSimple(AmqpSinkSettings settings, int repliesPerMessage = 1)
        {
            return 
                Flow.Create<ByteString>()
                    .Select(bytes => new OutgoingMessage(bytes, false, false))
                    .ViaMaterialized(AtMostOnceFlow(settings, 1, repliesPerMessage), Keep.Right)
                    .Select(_ => _.Bytes);
        }
    }
}