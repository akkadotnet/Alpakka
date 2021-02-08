using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using Docker.DotNet;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    [CollectionDefinition("StorageQueueSpec")]
    public sealed class StorageQueueSpecFixture : ICollectionFixture<AzureFixture>
    {
    }

    public enum OperatingSystem
    {
        NotInitialized,
        Windows,
        Linux,
        Unknown
    }

    public sealed class AzureFixture : IAsyncLifetime, IDisposable
    {
        private readonly List<string> _variables = new List<string>();

        private OperatingSystem _os;
        protected OperatingSystem OperatingSystem
        {
            get
            {
                if (_os != OperatingSystem.NotInitialized)
                    return _os;
                _os =
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OperatingSystem.Linux :
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OperatingSystem.Windows :
                    OperatingSystem.Unknown;
                return _os;
            }
        }

        protected readonly string AzuriteContainerName = $"azurite-{Guid.NewGuid():N}";
        protected DockerClient Client;

        protected string ImageName { 
            get
            {
                switch (OperatingSystem)
                {
                    case OperatingSystem.Linux:
                        return "mcr.microsoft.com/azure-storage/azurite";
                    case OperatingSystem.Windows:
                        return "akkadotnet/azure-storage-emulator";
                    default:
                        throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");
                }
            }
        }

        protected string ImageTag => "latest";

        protected string AzureImageName => $"{ImageName}:{ImageTag}";

        private bool? _useDocker = null;
        public bool UseDockerContainer
        {
            get
            {
                if (!_useDocker.HasValue)
                {
                    var env = Environment.GetEnvironmentVariable("ALPAKKA_AZURE_TEST_USEDOCKER")?.ToLowerInvariant();

                    // defaults to use docker, only turn off docker support if it is exactly set.
                    _useDocker = env == null || (env != "false" && env != "no" && env != "off");
                }

                return _useDocker.Value;
            }
        }

        public string AccountName { get; } = "devstoreaccount1";
        public string AccountKey { get; } =
            "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
        public string HostName { get; } = "127.0.0.1";

        private int _blobHostPort = -1;
        public int BlobHostPort
        {
            get
            {
                if (!UseDockerContainer) return 10000;

                if(_blobHostPort == -1)
                    _blobHostPort = ThreadLocalRandom.Current.Next(10000, 15000);
                return _blobHostPort;
            }
        }

        public int QueueHostPort => UseDockerContainer ? BlobHostPort + 1 : 10001;
        public int TableHostPort => UseDockerContainer ? BlobHostPort + 2 : 10002;
        public Uri BlobEndpoint => new Uri($"http://{HostName}:{BlobHostPort}/devstoreaccount1");
        public Uri QueueEndpoint => new Uri($"http://{HostName}:{QueueHostPort}/devstoreaccount1");
        public Uri QueueUri => new Uri($"http://{HostName}:{QueueHostPort}/devstoreaccount1/testqueue");
        public Uri TableEndpoint => new Uri($"http://{HostName}:{TableHostPort}/devstoreaccount1");
        public string ConnectionString { 
            get
            {
                var result =
                    $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};BlobEndpoint={BlobEndpoint};QueueEndpoint={QueueEndpoint};";
                if (OperatingSystem == OperatingSystem.Windows)
                    result += $"TableEndpoint={TableEndpoint};";
                return result;
            }
        }

        private DockerClientConfiguration Config
        {
            get
            {
                switch (OperatingSystem)
                {
                    case OperatingSystem.Linux:
                        return new DockerClientConfiguration(new Uri("unix://var/run/docker.sock"));
                    case OperatingSystem.Windows:
                        return new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine"));
                    default:
                        throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");
                }
            }
        }

        public AzureFixture()
        {
            if (!File.Exists("environment.json"))
                return;

            using (var file = File.OpenText("environment.json"))
            {
                var reader = new JsonTextReader(file);
                var jObject = JObject.Load(reader);

                var variables = jObject.Children<JProperty>();
                foreach (var variable in variables)
                {
                    var value = Environment.GetEnvironmentVariable(variable.Name);
                    if (value != null) continue;

                    _variables.Add(variable.Name);
                    Environment.SetEnvironmentVariable(variable.Name, variable.Value.ToString());
                }
            }

        }

        public async Task InitializeAsync()
        {
            if (!UseDockerContainer)
                return;

            Client = Config.CreateClient();

            var images = await Client.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>()
                {
                    ["reference"] = new Dictionary<string, bool>()
                    {
                        [AzureImageName] = true
                    }
                }
            });
            if (images.Count == 0)
                await Client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = ImageName, Tag = ImageTag }, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));

            var exposedPorts = new Dictionary<string, EmptyStruct>
            {
                { "10000/tcp", new EmptyStruct() }, { "10001/tcp", new EmptyStruct() },
            };
            if (OperatingSystem == OperatingSystem.Windows)
                exposedPorts.Add("10002/tcp", new EmptyStruct());

            var portBindings = new Dictionary<string, IList<PortBinding>>
            {
                { "10000/tcp", new List<PortBinding> { new PortBinding { HostPort = $"{BlobHostPort}" } } },
                { "10001/tcp", new List<PortBinding> { new PortBinding { HostPort = $"{QueueHostPort}" } } },
            };
            if (OperatingSystem == OperatingSystem.Windows)
                portBindings.Add(
                    "10002/tcp",
                    new List<PortBinding> { new PortBinding { HostPort = $"{TableHostPort}" } });

            // create the container
            await Client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = ImageName,
                Name = AzuriteContainerName,
                Tty = true,
                ExposedPorts = exposedPorts,
                HostConfig = new HostConfig { PortBindings = portBindings }
            });

            // start the container
            await Client.Containers.StartContainerAsync(AzuriteContainerName, new ContainerStartParameters());

            // Provide a 30 second startup delay
            //await Task.Delay(TimeSpan.FromSeconds(30));
        }

        public async Task<bool> StopContainer()
            => !UseDockerContainer || await Client.Containers.StopContainerAsync(AzuriteContainerName, new ContainerStopParameters());

        public async Task<bool> StartContainer()
            => !UseDockerContainer || await Client.Containers.StartContainerAsync(AzuriteContainerName, new ContainerStartParameters());

        public async Task DisposeAsync()
        {
            if (Client != null)
            {
                await Client.Containers.StopContainerAsync(AzuriteContainerName, new ContainerStopParameters());
                await Client.Containers.RemoveContainerAsync(AzuriteContainerName,
                    new ContainerRemoveParameters { Force = true });
                Client.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var variable in _variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }
    }
}
