using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Akka.Actor;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public abstract class QueueSpecBase : Akka.TestKit.Xunit2.TestKit
    {
        protected QueueSpecBase(AzureFixture fixture, ITestOutputHelper output) : base((ActorSystem)null, output)
        {
            Materializer = Sys.Materializer();

            var storageAccount = CloudStorageAccount.Parse(fixture.ConnectionString);
            var client = storageAccount.CreateCloudQueueClient();
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
