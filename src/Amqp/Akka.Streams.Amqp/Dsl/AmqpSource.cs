using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.Dsl
{
    public static class AmqpSource
    {
        /// <summary>
        /// The `committableSource` makes it possible to commit (ack/nack) messages to RabbitMQ.
        /// This is useful when "at-least once delivery" is desired, as each message will likely be
        /// delivered one time but in failure cases could be duplicated.
        /// If you commit the offset before processing the message you get "at-most once delivery" semantics,
        /// and for that there is a <see cref="AtMostOnceSource"/>.
        /// Compared to auto-commit, this gives exact control over when a message is considered consumed.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="bufferSize"></param>
        /// <returns>TBD</returns>
        public static Source<CommittableIncomingMessage, NotUsed> CommittableSource(IAmqpSourceSettings settings, int bufferSize)
        {
            return Source.FromGraph(new AmqpSourceStage(settings, bufferSize));
        }

        /// <summary>
        /// Convenience for "at-most once delivery" semantics. Each message is acked to RabbitMQ
        /// before it is emitted downstream.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        public static Source<IncomingMessage, NotUsed> AtMostOnceSource(IAmqpSourceSettings settings, int bufferSize)
        {
            return CommittableSource(settings, bufferSize)
                .SelectAsync(1, async cm =>
                {
                    await cm.Ack();
                    return cm.Message;
                });
        }
    }
}