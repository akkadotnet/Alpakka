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
using DotNet.Testcontainers.Containers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Testcontainers.RabbitMq;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Amqp.Tests
{
    [CollectionDefinition("AmqpSpec")]
    public sealed class AmqpSpecFixture : ICollectionFixture<AmqpFixture>
    {
    }

    public class AmqpFixture : IAsyncLifetime
    {
        public string ConnectionString => Container!.GetConnectionString();
        public string HostName => Container!.Hostname;
        public int AmqpPort => Container!.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

        public string UserName => "guest";
        public string Password => "guest";
        
        protected readonly string RabbitContainerName = $"rabbit-{Guid.NewGuid():N}";
        
        public RabbitMqContainer? Container { get; protected set; }

        public async Task InitializeAsync()
        {
            Container = new RabbitMqBuilder()
                .WithName(RabbitContainerName)
                .Build();
            
            await Container.StartAsync();
        }

        public async Task DisposeAsync()
        {
            if (Container != null)
            {
                await Container.StopAsync();
            }
        }
    }
}
