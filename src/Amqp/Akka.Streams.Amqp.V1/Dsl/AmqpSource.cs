using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.V1.Dsl
{
    public static class AmqpSource
    {
        public static Source<T, NotUsed> Create<T>(IAmqpSourceSettings<T> sourceSettings)
        {
            return Source.FromGraph(new AmqpSourceStage<T>(sourceSettings));
        }
    }
}
