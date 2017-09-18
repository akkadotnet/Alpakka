using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.Dsl
{
    public static class AmqpRpcFlow
    {
        /// <summary>
        /// Create an [[https://www.rabbitmq.com/tutorials/tutorial-six-java.html RPC style flow]] for processing and communicating
        /// over a rabbitmq message bus. This will create a private queue, and add the reply-to header to messages sent out.
        /// 
        /// This stage materializes to a <see cref="Task{String}"/>, which is the name of the private exclusive queue used for RPC communication.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="bufferSize"></param>
        /// <param name="repliesPerMessage">The number of responses that should be expected for each message placed on the queue. This
        /// can be overridden per message by including <code>expectedReplies</code> in the the header of the <see cref="OutgoingMessage"/></param>
        /// <returns>TBD</returns>
        public static Flow<OutgoingMessage, IncomingMessage, Task<string>> Create(AmqpSinkSettings settings,
            int bufferSize, int repliesPerMessage = 1)
        {
            return Flow.FromGraph(new AmqpRpcFlowStage(settings, bufferSize, repliesPerMessage));
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
        public static Flow<ByteString, ByteString, Task<string>> CreateSimple(AmqpSinkSettings settings,
            int repliesPerMessage = 1)
        {
            return Flow.Create<ByteString, Task<string>>().Select(bytes => new OutgoingMessage(bytes, false, false))
                .Via(Create(settings, 1, repliesPerMessage)).Select(_ => _.Bytes);
        }
    }
}
