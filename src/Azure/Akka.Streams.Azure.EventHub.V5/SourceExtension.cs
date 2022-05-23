using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

namespace Akka.Streams.Azure.EventHub.V5
{
    public static class SourceExtension
    {
        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a <see cref="EventHubSink"/>.
        /// The returned <see cref="Task"/> will be completed with Success when reaching the
        /// normal end of the stream, or completed with Failure if there is a failure signaled in the stream.
        /// </summary>
        public static Task ToEventHub<TMat>(this Source<IEnumerable<EventData>, TMat> source, EventHubProducerClient producer, IMaterializer materializer)
        {
            return source.RunWith(new EventHubSink(producer), materializer);
        }
    }
}
