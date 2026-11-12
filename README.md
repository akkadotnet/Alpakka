# Alpakka: Akka Streams connectors

Alpakka connects [Akka.NET Streams](https://getakka.net/articles/streams/introduction.html) to messaging systems, cloud services and local data formats. Each package README explains its actual APIs, defaults, client ownership, limits and local tests. Packages built from current source include that README; older published releases may omit it.

[Documentation](https://alpakka.getakka.net/) · [Running tests](docs/community/Testing.md) · [Legacy package migrations](docs/documentation/LegacyPackages.md)

## Maintained packages

| Connector and README | NuGet package | Version | Downloads |
| --- | --- | --- | --- |
| [AMQP 1.0](src/Amqp/Akka.Streams.Amqp.V1/README.md) | [Akka.Streams.Amqp.V1](https://www.nuget.org/packages/Akka.Streams.Amqp.V1) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Amqp.V1.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Amqp.V1) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Amqp.V1.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Amqp.V1) |
| [RabbitMQ](src/Amqp/Akka.Streams.Amqp.RabbitMq/README.md) | [Akka.Streams.Amqp.RabbitMq](https://www.nuget.org/packages/Akka.Streams.Amqp.RabbitMq) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Amqp.RabbitMq.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Amqp.RabbitMq) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Amqp.RabbitMq.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Amqp.RabbitMq) |
| [Azure Event Hubs](src/Azure/Akka.Streams.Azure.EventHub/README.md) | [Akka.Streams.Azure.EventHub](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Azure.EventHub.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Azure.EventHub.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub) |
| [Azure Service Bus](src/Azure/Akka.Streams.Azure.ServiceBus/README.md) | [Akka.Streams.Azure.ServiceBus](https://www.nuget.org/packages/Akka.Streams.Azure.ServiceBus) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Azure.ServiceBus.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.ServiceBus) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Azure.ServiceBus.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.ServiceBus) |
| [Azure Queue Storage](src/Azure/Akka.Streams.Azure.StorageQueue/README.md) | [Akka.Streams.Azure.StorageQueue](https://www.nuget.org/packages/Akka.Streams.Azure.StorageQueue) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Azure.StorageQueue.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.StorageQueue) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Azure.StorageQueue.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Azure.StorageQueue) |
| [Amazon Kinesis](src/AWS/Kinesis/Akka.Streams.Kinesis/README.md) | [Akka.Streams.Kinesis](https://www.nuget.org/packages/Akka.Streams.Kinesis) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Kinesis.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Kinesis) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Kinesis.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Kinesis) |
| [Amazon SNS](src/AWS/SNS/Akka.Streams.Sns/README.md) | [Akka.Streams.Sns](https://www.nuget.org/packages/Akka.Streams.Sns) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Sns.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Sns) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Sns.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Sns) |
| [Amazon SQS](src/AWS/SQS/Akka.Streams.SQS/README.md) | [Akka.Streams.SQS](https://www.nuget.org/packages/Akka.Streams.SQS) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.SQS.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.SQS) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.SQS.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.SQS) |
| [CSV](src/Csv/Akka.Streams.Csv/README.md) | [Akka.Streams.Csv](https://www.nuget.org/packages/Akka.Streams.Csv) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Csv.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Csv) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Csv.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Csv) |
| [File](src/File/Akka.Streams.File/README.md) | [Akka.Streams.File](https://www.nuget.org/packages/Akka.Streams.File) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.File.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.File) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.File.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.File) |
| [SignalR](src/SignalR/Akka.Streams.SignalR/README.md) | [Akka.Streams.SignalR](https://www.nuget.org/packages/Akka.Streams.SignalR) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.SignalR.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.SignalR) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.SignalR.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.SignalR) |
| [XML](src/Xml/Akka.Streams.Xml/README.md) | [Akka.Streams.Xml](https://www.nuget.org/packages/Akka.Streams.Xml) | [![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Xml.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Xml) | [![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Xml.svg?style=flat-square)](https://www.nuget.org/packages/Akka.Streams.Xml) |

The libraries retain their .NET Standard 2.0 and .NET 6.0 assets, except SignalR, which targets .NET 6.0. Test and sample hosts may use newer frameworks independently of the shipped libraries. Check a package README before combining SDKs: the SQS adapter uses AWS SDK v3, while SNS and Kinesis use v4.

## Compatibility

Changes follow extend-only design: preserve public signatures, shipped targets, wire formats, documented defaults and client ownership. Add overloads or new types for new behavior. A dependency upgrade also needs source, binary and behavior checks; a compatible-looking NuGet framework badge is not sufficient evidence.

Required checks compare against a released package, execute an unchanged consumer against the candidate, inspect the actual NuGet archive and compile its README examples. Integration coverage uses Aspire.Hosting.Testing or Testcontainers where a real service boundary is needed, with owned resources, discovered endpoints, bounded readiness and visible failures. CSV, XML and local filesystem tests use the actual local operations they exercise.

Deprecation guidance must identify a verified replacement and migration requirements. Keep published artifacts available; do not remove working APIs, add error-level obsolete attributes or silently change targets. See the [package migration guide](docs/documentation/LegacyPackages.md).

## Build and test

Use the SDK selected by [global.json](global.json), then run:

```shell
dotnet restore Alpakka.slnx
dotnet build Alpakka.slnx -c Release
```

See [the test guide](docs/community/Testing.md) for individual suites, Docker requirements, Windows coverage and the required package/consumer gates. Containers and cloud emulators are started by their fixtures; no cloud account or separately installed broker is needed for those suites.

## Contributions

Contributions are welcome. Follow the [Akka.NET contribution guide](https://github.com/akkadotnet/akka.net/blob/dev/CONTRIBUTING.md), retain useful existing assertions, and provide regression evidence for behavior changes.
