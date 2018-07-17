using Akka.Streams.Dsl;

namespace Akka.Streams.Amqp.V1.Dsl
{
    public static class AmpqSource<T>
    {
        public static Source<T, NotUsed> Create(IAmqpSourceSettings<T> sourceSettings)
        {
            return Source.FromGraph(new AmqpSourceStage<T>(sourceSettings));
        }
    }
}
