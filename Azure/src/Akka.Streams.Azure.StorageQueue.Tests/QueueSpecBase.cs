using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public abstract class QueueSpecBase : Akka.TestKit.Xunit.TestKit
    {
        protected QueueSpecBase()
        {
            Materializer = Sys.Materializer();

            var client = CloudStorageAccount.DevelopmentStorageAccount.CreateCloudQueueClient();
            Queue = client.GetQueueReference("testqueue");
            Queue.CreateIfNotExists();
        }

        public CloudQueue Queue { get; }

        protected ActorMaterializer Materializer { get; }

        protected override void Dispose(bool disposing)
        {
            Queue.DeleteIfExists();

            base.Dispose(disposing);
        }
    }
}
