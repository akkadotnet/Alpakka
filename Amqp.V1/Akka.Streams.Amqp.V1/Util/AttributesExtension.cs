using Akka.Streams.Supervision;

namespace Akka.Streams.Amqp.V1.Util
{
    public static class AttributesExtensions
    {
        public static Decider GetDeciderOrDefault(this Attributes attributes)
        {
            var attr = attributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
            return attr?.Decider ?? Deciders.StoppingDecider;
        }
    }
}
