using System.Threading.Tasks;
using Azure.Storage.Queues;
using Akka.Actor;
using Azure.Storage;
using Azure.Storage.Sas;
using Microsoft.Extensions.Azure;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public abstract class QueueSpecBase : Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
    {
        protected QueueSpecBase(ITestOutputHelper output) : base((ActorSystem)null, output)
        {
            Materializer = Sys.Materializer();
            Queue = new QueueClient("UseDevelopmentStorage=true", "testqueue");
        }

        public QueueClient Queue { get; }

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
