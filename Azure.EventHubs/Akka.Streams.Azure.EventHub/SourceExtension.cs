using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Microsoft.Azure.EventHubs;

namespace Akka.Streams.Azure.EventHub
{
    public static class SourceExtension
    {
        /// <summary>
        /// Shurtcut for running this <see cref="Source{TOut,TMat}"/> with a <see cref="EventHubSink"/>.
        /// The returned <see cref="Task"/> will be completed with Success when reaching the
        /// normal end of the stream, or completed with Failure if there is a failure signaled in the stream.
        /// </summary>
        public static Task ToEventHub<TMat>(this Source<IEnumerable<EventData>, TMat> source, EventHubClient client, IMaterializer materializer)
        {
            return source.RunWith(new EventHubSink(client), materializer);
        }
    }
}
