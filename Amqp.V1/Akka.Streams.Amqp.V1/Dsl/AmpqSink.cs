using Akka.Streams.Dsl;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.V1.Dsl
{
    public static class AmpqSink<T>
    {
        public static Sink<T, Task> Create(IAmpqSinkSettings<T> sourceSettings)
        {
            return Sink.FromGraph(new AmqpSinkStage<T>(sourceSettings));
        }
    }
}
