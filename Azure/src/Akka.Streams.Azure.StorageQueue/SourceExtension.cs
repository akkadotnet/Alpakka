using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Akka.Streams.Azure.StorageQueue
{
    public static class SourceExtension
    {
        /// <summary>
        /// Shurtcut for running this <see cref="Source{TOut,TMat}"/> with a <see cref="QueueSink"/>.
        /// The returned <see cref="Task"/> will be completed with Success when reaching the
        /// normal end of the stream, or completed with Failure if there is a failure signaled in the stream.
        /// </summary>
        public static Task ToStorageQueue<TMat>(this Source<CloudQueueMessage, TMat> source, CloudQueue queue,
            IMaterializer materializer, AddRequestOptions options = null)
        {
            return source.RunWith(new QueueSink(queue, options), materializer);
        }
    }
}
