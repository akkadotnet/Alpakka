using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

namespace Akka.Streams.Amqp.Tests
{
    [CollectionDefinition("AmqpSpec")]
    public sealed class AmqpSpecFixture : ICollectionFixture<AmqpFixture>
    {
    }

    public enum OperatingSystem
    {
        NotInitialized,
        Windows,
        Linux,
        Unknown
    }

    public class AmqpFixture : IAsyncLifetime, IDisposable
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

        protected readonly string AqmpContainerName = $"amqp-{Guid.NewGuid():N}";
        protected DockerClient Client;

        protected string AmqpImageName 
        {
            get
            {
                switch (OperatingSystem)
                {
                    case OperatingSystem.Windows:
                        return "arkatufus/rabbitmq";
                    case OperatingSystem.Linux:
                        return "arkatufus/rabbitmq-linux";
                    default:
                        throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");
                }
            }
        }

        protected string AmqpImageTag
        {
            get
            {
                switch (OperatingSystem)
                {
                    case OperatingSystem.Windows:
                        return "latest";
                    case OperatingSystem.Linux:
                        return "latest";
                    default:
                        throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");
                }
            }
        }

        private bool? _useDocker = null;
        public bool UseDockerContainer
        {
            get
            {
                if (!_useDocker.HasValue)
                {
                    var env = Environment.GetEnvironmentVariable("ALPAKKA_AMQP_TEST_USEDOCKER")?.ToLowerInvariant();
                    
                    _useDocker = env == null || (env != "false" && env != "no" && env != "off");
                    Console.WriteLine($"Environment Variable: ALPAKKA_AMQP_TEST_USEDOCKER = {env ?? "null"}");
                }

                return _useDocker.Value;
            }
        }

        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string HostName { get; set; } = "127.0.0.1";

        public int AmqpPort => 5672;

        public int AmqpsPort => 5671;

        public int EpmdPort => 4369;

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

        public AmqpFixture()
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

            Console.WriteLine("Using Dockerized RabbitMQ Broker");

            Client = Config.CreateClient();

            var images = await Client.Images.ListImagesAsync(new ImagesListParameters { MatchName = AmqpImageName });
            if (images.Count == 0)
                await Client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = AmqpImageName, Tag = AmqpImageTag }, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));

            var exposedPorts = new Dictionary<string, EmptyStruct>
            {
                { "4369/tcp", new EmptyStruct() },
                { "5671/tcp", new EmptyStruct() },
                { "5672/tcp", new EmptyStruct() },
            };

            var portBindings = new Dictionary<string, IList<PortBinding>>
            {
                { "4369/tcp", new List<PortBinding> { new PortBinding { HostPort = $"{EpmdPort}" } } },
                { "5671/tcp", new List<PortBinding> { new PortBinding { HostPort = $"{AmqpsPort}" } } },
                { "5672/tcp", new List<PortBinding> { new PortBinding { HostPort = $"{AmqpPort}" } } },
            };

            // create the container
            await Client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = $"{AmqpImageName}:{AmqpImageTag}",
                Name = AqmpContainerName,
                Tty = true,
                ExposedPorts = exposedPorts,
                HostConfig = new HostConfig { PortBindings = portBindings }
            });

            // start the container
            await Client.Containers.StartContainerAsync(AqmpContainerName, new ContainerStartParameters());


            // Ping server continuously until either we timed out or the server is up.
            // RabbitMQ Windows image takes a very long time to spin up!
            var gracePeriod = TimeSpan.FromMinutes(2);
            var tcpClient = new TcpClient();
            var stopWatch = Stopwatch.StartNew();
            try
            {
                while (stopWatch.Elapsed < gracePeriod)
                {
                    // Ping the server every second
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    try
                    {
                        await tcpClient.ConnectAsync(HostName, AmqpPort);
                        tcpClient.Close();
                        break;
                    }
                    catch (SocketException _)
                    {
                        // no-op
                    }
                }
                stopWatch.Stop();

                if (stopWatch.Elapsed >= gracePeriod)
                    throw new Exception($"RabbitMQ server did not respond within grace period of {gracePeriod.TotalSeconds} seconds.");
            }
            finally
            {
                tcpClient.Dispose();
            }

            // Provide a 60 second startup delay
            // await Task.Delay(TimeSpan.FromSeconds(60));
        }

        public async Task<bool> StopContainer()
            => !UseDockerContainer || await Client.Containers.StopContainerAsync(AqmpContainerName, new ContainerStopParameters());

        public async Task<bool> StartContainer()
            => !UseDockerContainer || await Client.Containers.StartContainerAsync(AqmpContainerName, new ContainerStartParameters());

        public async Task DisposeAsync()
        {
            if (Client != null)
            {
                await Client.Containers.StopContainerAsync(AqmpContainerName, new ContainerStopParameters());
                await Client.Containers.RemoveContainerAsync(AqmpContainerName,
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
