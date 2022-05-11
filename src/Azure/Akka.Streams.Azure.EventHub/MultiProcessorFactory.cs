using Akka.Streams.Dsl;
using Microsoft.Azure.EventHubs.Processor;

namespace Akka.Streams.Azure.EventHub
{
    /// <summary>
    /// A processor factory that always creates a new stream
    /// </summary>
    public class MultiProcessorFactory : IEventProcessorFactory
    {
        private readonly IRunnableGraph<IEventProcessor> _graph;
        private readonly IMaterializer _materializer;

        /// <summary>
        /// Creates a new instance of the <see cref="MultiProcessorFactory"/>
        /// </summary>
        /// <param name="graph">The graph that should be run</param>
        /// <param name="materializer">The materializer</param>
        public MultiProcessorFactory(IRunnableGraph<IEventProcessor> graph, IMaterializer materializer)
        {
            _graph = graph;
            _materializer = materializer;
        }

        public IEventProcessor CreateEventProcessor(PartitionContext context) => _graph.Run(_materializer);
    }
}
