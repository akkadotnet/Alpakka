using Microsoft.Azure.EventHubs.Processor;

namespace Akka.Streams.Azure.EventHub
{
    /// <summary>
    /// A processor factory that always returns the given processor
    /// </summary>
    public sealed class SingleProcessorFactory : IEventProcessorFactory
    {
        private readonly IEventProcessor _processor;

        /// <summary>
        /// Creates a new instance of the <see cref="SingleProcessorFactory"/>
        /// </summary>
        /// <param name="processor">The processor</param>
        public SingleProcessorFactory(IEventProcessor processor)
        {
            _processor = processor;
        }

        public IEventProcessor CreateEventProcessor(PartitionContext context) => _processor;
    }
}
