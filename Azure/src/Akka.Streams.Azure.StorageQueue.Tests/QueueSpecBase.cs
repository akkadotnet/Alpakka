using System;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public abstract class QueueSpecBase : Akka.TestKit.Xunit2.TestKit
    {
        protected QueueSpecBase()
        {
            Materializer = Sys.Materializer();

            var client = CloudStorageAccount.DevelopmentStorageAccount.CreateCloudQueueClient();
            Queue = client.GetQueueReference($"testqueue-{Guid.NewGuid()}");
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
