using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.Dsl
{
    public static class AmqpSource
    {
        /// <summary>
        /// Creates an <see cref="AmqpSource"/> with given settings and buffer size.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="bufferSize"></param>
        /// <returns>TBD</returns>
        public static Source<IncomingMessage, NotUsed> Create(IAmqpSourceSettings settings, int bufferSize)
        {
            return Source.FromGraph(new AmqpSourceStage(settings, bufferSize));
        } 
    }
}
