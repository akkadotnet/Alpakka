---
uid: Testing.md
title: Running Alpakka Test Suite
---

# Running the Alpakka test suites

Use the SDK selected by the repository's global.json. Build from the repository root:

```shell
dotnet restore Alpakka.slnx
dotnet build Alpakka.slnx -c Release
```

The connector suites own their test resources. Messaging/cloud fixtures use discovered endpoints and pinned images; they do not require a separately installed broker, Azure Storage Emulator, fixed localhost ports or a cloud account. Docker must be running Linux containers for the container suites. Service Bus and Event Hubs use their emulator license-acceptance configuration in the test AppHost.

## Select a suite

Run the command for the connector being changed from the repository root. These are the modern test-host targets; they do not change the library's shipped targets.

| Connector | Command | Boundary |
| --- | --- | --- |
| AMQP 1.0 | `dotnet test src/Amqp/Akka.Streams.Amqp.V1.Tests/Akka.Streams.Amqp.V1.Tests.csproj -c Release --framework net10.0` | Testcontainers ActiveMQ Artemis |
| RabbitMQ | `dotnet test src/Amqp/Akka.Streams.Amqp.RabbitMq.Tests/Akka.Streams.Amqp.RabbitMq.Tests.csproj -c Release --framework net10.0` | Testcontainers RabbitMQ |
| Azure Service Bus | `dotnet test src/Azure/Akka.Streams.Azure.ServiceBus.Tests/Akka.Streams.Azure.ServiceBus.Tests.csproj -c Release --framework net10.0` | Aspire.Hosting.Testing, Service Bus emulator and SQL companion |
| Azure Event Hubs | `dotnet test src/Azure/Akka.Streams.Azure.EventHub.Tests/Akka.Streams.Azure.EventHub.Tests.csproj -c Release --framework net10.0` | Aspire.Hosting.Testing, Event Hubs emulator and Blob checkpoints |
| Azure Queue Storage | `dotnet test src/Azure/Akka.Streams.Azure.StorageQueue.Tests/Akka.Streams.Azure.StorageQueue.Tests.csproj -c Release --framework net10.0` | Testcontainers Azurite |
| SQS | `dotnet test src/AWS/SQS/Akka.Streams.SQS.Tests/Akka.Streams.SQS.Tests.csproj -c Release --framework net10.0` | Controlled clients plus Testcontainers Floci |
| SNS contracts | `dotnet test src/AWS/SNS/Akka.Streams.Sns.Tests/Akka.Streams.Sns.Tests.csproj -c Release --framework net10.0` | Controlled publication/completion contracts |
| SNS subscriptions | `dotnet test src/AWS/SNS/Akka.Streams.Sns.IntegrationTests/Akka.Streams.Sns.IntegrationTests.csproj -c Release --framework net10.0` | Native Floci SNS→SQS, isolated SDK graph |
| Kinesis | `dotnet test src/AWS/Kinesis/Akka.Streams.Kinesis.Tests/Akka.Streams.Kinesis.Tests.csproj -c Release --framework net10.0` | Controlled clients plus native Floci put/get/iterators |
| SignalR | `dotnet test src/SignalR/Akka.Streams.SignalR.Tests/Akka.Streams.SignalR.Tests.csproj -c Release --framework net10.0` | TestServer WebSockets and Aspire-owned Kestrel; no Docker |
| CSV | `dotnet test src/Csv/Akka.Streams.Csv.Tests/Akka.Streams.Csv.Tests.csproj -c Release --framework net10.0` | Actual parser/formatter stages |
| XML | `dotnet test src/Xml/Akka.Streams.Xml.Tests/Akka.Streams.Xml.Tests.csproj -c Release --framework net10.0` | Actual parser/event stages |
| File | `dotnet test src/File/Akka.Streams.File.Tests/Akka.Streams.File.Tests.csproj -c Release --framework net10.0` | Temporary files and OS notifications; native inotify release on Linux |

The SQS adapter uses SDK v3. SNS/Kinesis use v4, and the SNS integration observer has a separate v4 graph. Keep those project/process boundaries when adding tests; combining the adapters in one process is not a substitute for verifying their dependency ranges.

Azure emulator fixtures select protocol versions supported by the pinned images where needed. Keep API-version checks enabled. An emulator can establish payload, settlement/checkpoint or lifecycle behavior only for the exercised operations; it does not establish cloud identity, scale, quotas or durability after emulator restart.

## Required CI and package checks

[The PR pipeline](https://github.com/akkadotnet/Alpakka/blob/dev/build-system/pr-validation.yaml) owns twelve explicit connector jobs. The SNS job runs two test projects. Their TRX guards require named cases and reject missing, empty, duplicated, failed or skipped results. The corresponding projects are excluded from the general incremental lane because those explicit jobs own them; an empty general selection is not additional integration coverage.

Each job also packs the actual connector with API validation against released 1.5.59, checks the README and retained TFM assets, recompiles a consumer from the package, and executes its preserved original binary with candidate dependencies. Packaged C# examples are compiled against the exact archive using isolated caches. CSV, XML and SignalR jobs also execute the relevant packaged examples against real files or WebSockets. The File job separately executes its unchanged compatibility consumer against the real filesystem. Inspect a connector's actual inline pipeline steps when reproducing all its gates locally; `dotnet test` alone does not establish those packaging/compatibility claims.

Windows jobs retain the appropriate .NET Framework compile/runtime checks for the .NET Standard libraries and run pure contracts where applicable. SignalR's library asset is net6.0 and its test hosts run net10.0; there is no net48 SignalR claim. Container integration runs on Linux in CI. The File suite runs actual filesystem cases on both Windows and Linux.

## Changes to dependencies or fixtures

Retain useful existing cases and assertions. Use a service-boundary assertion for a service guarantee: materialized completion alone does not prove broker delivery, deletion, acknowledgment or checkpoint persistence. Hold native state or use a positive control before testing its absence.

Fixtures must own only their resources, use bounded startup and cleanup, and surface setup/cleanup errors. An unavailable endpoint or image must fail the job rather than silently skip it. Do not hide warnings, disable API checks or accept a no-tests run to obtain a green result.

For a driver update, inspect source/binary compatibility and the resolved/runtime dependency graph, then run the connector's native and package gates. Preserve the old consumer binary while resolving dependencies for its own target. Retaining a net6.0 asset does not by itself prove execution on the .NET 6 runtime; collect actual runtime evidence when that support is affected. Keep SDK support/audit warnings visible and distinguish them from passed behavioral checks.

The [historical Docker build guide](BuildingDockerImages.md) remains available for old checkouts. Its custom images and environment.json switches are not the setup procedure for these current fixtures.
