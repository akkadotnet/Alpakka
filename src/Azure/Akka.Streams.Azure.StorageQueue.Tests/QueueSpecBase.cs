using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Akka.Actor;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public abstract class QueueSpecBase : Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
    {
        protected QueueSpecBase(AzureFixture fixture, ITestOutputHelper output) : base((ActorSystem)null, output)
        {
            Materializer = Sys.Materializer();

            var storageAccount = CloudStorageAccount.Parse(fixture.ConnectionString);
            var client = storageAccount.CreateCloudQueueClient();
            Queue = client.GetQueueReference("testqueue");
        }

        public CloudQueue Queue { get; }

        protected ActorMaterializer Materializer { get; }

        public async Task InitializeAsync()
        {
            await Queue.CreateIfNotExistsAsync();
        }

        public async Task DisposeAsync()
        {
            await Queue.DeleteIfExistsAsync();
        }
    }
}
