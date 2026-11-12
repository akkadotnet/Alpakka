using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Akka.Actor;
using Testcontainers.Azurite;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    public abstract class QueueSpecBase : Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
    {
        private AzuriteContainer _container;
        private QueueClient _queue;

        protected QueueSpecBase(ITestOutputHelper output) : base((ActorSystem)null, output)
        {
            Materializer = Sys.Materializer();
        }

        public QueueClient Queue => _queue ?? throw new InvalidOperationException("Azurite has not initialized.");

        protected ActorMaterializer Materializer { get; }

        public async Task InitializeAsync()
        {
            _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0").Build();
            try
            {
                using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await _container.StartAsync(startup.Token);
                _queue = new QueueClient(_container.GetConnectionString(), "testqueue");
                await _queue.CreateIfNotExistsAsync(cancellationToken: startup.Token);
            }
            catch (Exception startupException)
            {
                try
                {
                    await DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException("Azurite startup and cleanup both failed.", startupException, cleanupException);
                }

                throw;
            }
        }

        public async Task DisposeAsync()
        {
            var container = _container;
            _container = null;
            _queue = null;
            if (container is not null)
                await container.DisposeAsync();
        }
    }
}
