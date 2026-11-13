# Alpakka: Akka Streams connectors

Alpakka connects [Akka.NET Streams](https://getakka.net/articles/streams/introduction.html) to messaging systems, cloud services and local data formats. Its connectors provide sources, flows and sinks that you compose with the rest of your stream: consume messages, transform records, publish results, process files or exchange SignalR messages.

Choose the packages your application needs. Each package README explains its APIs, configuration, defaults, client ownership, delivery semantics and limitations, with examples and commands for its tests. Packages built from current source include that README; older published releases may omit it.

[Documentation](https://alpakka.getakka.net/) · [Running tests](docs/community/Testing.md) · [Legacy package migrations](docs/documentation/LegacyPackages.md)

## Contents

- [Getting started](#getting-started)
- [Maintained packages](#maintained-packages)
- [Examples](#examples)
- [Build and test](#build-and-test)
- [Compatibility and upgrades](#compatibility-and-upgrades)
- [Repository layout](#repository-layout)
- [Contributing](#contributing)
- [Documentation and support](#documentation-and-support)
- [License](#license)

## Getting started

1. Find your service or data format in the [package catalog](#maintained-packages).
2. Install that connector in your application directory. For example, to parse or format CSV:

   ```shell
   dotnet add package Akka.Streams.Csv
   ```

3. Follow the [CSV package examples](src/Csv/Akka.Streams.Csv/README.md), or your chosen connector's README, to create and materialize a stream. Messaging connectors also explain how to configure the broker or SDK client they use.

If you are new to streams, start with the [Akka.NET Streams introduction](https://getakka.net/articles/streams/introduction.html). Connector-specific acknowledgments, retries and buffering still matter: completing a stream does not universally mean that a remote peer has received or persisted every message.

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

## Examples

Package READMEs contain focused usage examples. The repository also includes runnable distributed samples:

| Sample | Entry point | Resources |
| --- | --- | --- |
| Azure Service Bus | [ServiceBus.AppHost](src/Azure/Examples/Simple/ServiceBus/ServiceBus.AppHost) | Producer, consumer, Service Bus emulator and SQL companion |
| Azure Event Hubs | [EventHub.AppHost](src/Azure/Examples/Simple/EventHub/EventHub.AppHost) | Producer, consumer, Event Hubs emulator and Blob checkpoint storage |
| SignalR | [SignalR.AppHost](src/SignalR/Examples/SignalR.AppHost) | ASP.NET Core application |

Use the repository SDK to run an AppHost, for example:

```shell
dotnet run --project src/Azure/Examples/Simple/ServiceBus/ServiceBus.AppHost/ServiceBus.AppHost.csproj
```

The Azure emulator samples require Docker with Linux containers. Check the relevant package README and sample configuration before using an external service or supplying credentials.

## Build and test

To work on Alpakka itself, install the SDK selected by [global.json](global.json), then run these commands from the repository root:

```shell
dotnet restore Alpakka.slnx
dotnet build Alpakka.slnx -c Release
```

Run the suite for the connector you are changing. A local CSV example is:

```shell
dotnet test src/Csv/Akka.Streams.Csv.Tests/Akka.Streams.Csv.Tests.csproj -c Release --framework net10.0
```

The [test guide](docs/community/Testing.md) lists every suite, Docker requirements, Windows coverage and the required package/consumer checks. Service fixtures own their containers or Aspire resources and discover their endpoints; those tests do not need a cloud account or separately installed broker. CSV, XML and filesystem suites exercise their local operations directly. SignalR tests use TestServer and Aspire-owned Kestrel without Docker.

## Compatibility and upgrades

Packages built from this revision require Akka.Streams 1.5.74 or later. Streams and TestKit package versions are aligned centrally.

The Akka.NET upgrade from the previous 1.5.60 minimum inherits upstream binary changes involving the `RetrySupport.Retry` overload with an `int randomFactor` and custom implementations of `ISourceQueue<T>`. See the [unreleased compatibility notes](RELEASE_NOTES.md) before upgrading an existing application.

Changes follow extend-only design: preserve public signatures, shipped targets, wire formats, documented defaults and client ownership. Add overloads or new types for new behavior. A dependency upgrade also needs source, binary and behavior checks; a compatible-looking NuGet framework badge is not sufficient evidence.

Required checks compare against a released package, execute an unchanged consumer against the candidate, inspect the actual NuGet archive and compile its README examples. Integration coverage uses Aspire.Hosting.Testing or Testcontainers where a real service boundary is needed, with owned resources, discovered endpoints, bounded readiness and visible failures. CSV, XML and local filesystem tests use the actual local operations they exercise.

Deprecation guidance must identify a verified replacement and migration requirements. Keep published artifacts available; do not remove working APIs, add error-level obsolete attributes or silently change targets. See the [package migration guide](docs/documentation/LegacyPackages.md).

For an existing application, read [release notes](RELEASE_NOTES.md), the package README and the [legacy migration guide](docs/documentation/LegacyPackages.md) before changing package IDs or SDK major versions. Retaining a library target does not extend an upstream SDK's support lifecycle. Dependency support and vulnerability limitations must remain visible even when behavioral checks pass.

## Repository layout

| Path | Purpose |
| --- | --- |
| [src](src) | Connector libraries, their tests and samples |
| [compatibility](compatibility) | Released-package consumer probes used by compatibility checks |
| [docs](docs) | DocFX documentation, contributor guidance and migration guides |
| [build-system](build-system) | CI, package validation, code standards and documentation builds |
| [Directory.Packages.props](Directory.Packages.props) | Central NuGet package versions |
| [global.json](global.json) | Repository SDK version |

## Contributing

Follow the [Akka.NET contribution guide](https://github.com/akkadotnet/akka.net/blob/dev/CONTRIBUTING.md) and Alpakka's [code standards](docs/community/CodeStandards.md). Keep changes focused, preserve useful existing assertions and add regression evidence for behavior changes. New service integration tests should use Aspire.Hosting.Testing or Testcontainers with owned resources and observable delivery or state assertions.

Use central package management for dependency changes and run the relevant connector's tests. Public APIs, library targets, defaults and ownership contracts require compatibility review; broader implementation redesign should be proposed separately.

## Documentation and support

- [Published documentation](https://alpakka.getakka.net/) and [documentation sources](docs/documentation/index.md)
- [Package migration guide](docs/documentation/LegacyPackages.md) for Channels, EventHub.V5 and SignalR.AspNetCore
- [Release notes](RELEASE_NOTES.md)
- [Report a bug or request a feature](https://github.com/akkadotnet/Alpakka/issues)

For a bug report, include the connector and SDK versions, target framework, a small reproduction and the observed stream behavior. Remove connection strings, keys and other credentials from logs and examples.

## License

Alpakka is licensed under [Apache License 2.0](LICENSE.md).
