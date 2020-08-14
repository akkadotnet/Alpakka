using Akka.Streams.Dsl;
using System.Threading.Tasks;

namespace Akka.Streams.Amqp.V1.Dsl
{
    public static class AmqpSink
    {
        public static Sink<T, Task> Create<T>(IAmqpSinkSettings<T> sourceSettings)
        {
            return Sink.FromGraph(new AmqpSinkStage<T>(sourceSettings));
        }
    }
}
