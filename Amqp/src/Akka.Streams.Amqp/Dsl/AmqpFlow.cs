using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.Dsl
{
    public static class AmqpFlow
    {
        /// <summary>
        /// Create a flow that publishes messages to RabbitMQ.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>TBD</returns>
        public static Flow<(OutgoingMessage, TPassThrough), TPassThrough, Task> Create<TPassThrough>(AmqpSinkSettings settings)
        {
            return Flow.FromGraph(new AmqpFlowStage<TPassThrough>(settings));
        }
    }
}