# Legacy package migrations

Package names and assembly identities matter during migration. Rebuild and test an application after replacing a package; do not replace a DLL with a differently named assembly and expect an unchanged executable to work. Published legacy artifacts remain available.

## Package status

The latest stable legacy versions were checked against NuGet's registration metadata on 12 November 2026.

| Legacy package | Latest legacy version | Current location | Migration requirements |
| --- | --- | --- | --- |
| [Akka.Streams.Channels](https://www.nuget.org/packages/Akka.Streams.Channels/1.5.37) | 1.5.37; already marked Legacy, with Akka.Streams as the alternate | Akka.Streams | Replace factory calls and remove the old namespace/package after rebuilding; see [Channels](Channels.md). |
| [Akka.Streams.Azure.EventHub.V5](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub.V5/1.5.28) | 1.5.28; no deprecation metadata was present | Akka.Streams.Azure.EventHub | The source namespace stays Akka.Streams.Azure.EventHub; the assembly identity changes. |
| [Akka.Streams.SignalR.AspNetCore](https://www.nuget.org/packages/Akka.Streams.SignalR.AspNetCore/1.5.15) | 1.5.15; no deprecation metadata was present | Akka.Streams.SignalR | Update namespace imports, rebuild, and check the host framework requirement. |

The last two package IDs are superseded names. This documentation does not claim that their NuGet deprecation metadata has already been published.

## EventHub.V5

The current [Event Hubs package](Azure/EventHub.md) uses Azure.Messaging.EventHubs. EventHub.V5 was an interim package name and used the same public `Akka.Streams.Azure.EventHub` namespace. Its published 1.5.28 package and the current adapter both provide .NET Standard 2.0 and .NET 6.0 assets.

Replace the `Akka.Streams.Azure.EventHub.V5` package reference with `Akka.Streams.Azure.EventHub`, remove any explicit references to the old assembly, and rebuild all consumers. Factory construction and context/checkpoint delegate examples were compiled and executed against both legacy 1.5.28 and current 1.5.59 packages without changing that source. These checks did not contact Azure; native delivery/checkpoint coverage belongs to the current connector's integration suite.

This is separate from migrating an application that still uses Microsoft.Azure.EventHubs, EventProcessorHost or the old IEventProcessor API. Those SDK APIs require an explicit migration to Azure.Messaging.EventHubs and its processor/checkpoint model. Follow the current package README rather than the historical EventProcessorHost examples.

## SignalR.AspNetCore

Use the current [ASP.NET Core SignalR package](SignalR.md). Update `using Akka.Streams.SignalR.AspNetCore` to `using Akka.Streams.SignalR`, including `.Internals` imports where they were used. Replace the package reference and rebuild; the assembly identity changes too.

The old 1.5.15 package supplied netcoreapp3.1 and net6.0 assets. The current package supplies net6.0. A netcoreapp3.1 application needs a host/framework migration before replacing the package; there is no drop-in current asset for that target.

Settings, events, routing, DI and hub/connector factory examples were compiled and executed with the old package and, after the namespace change, current 1.5.59. Those examples did not open network connections. The current connector's separate WebSocket integration tests cover transport behavior.

The OWIN `PersistentConnection`/`StreamConnection` examples describe an older SignalR integration. They are not APIs of the current typed-hub adapter. Use the complete server/client examples in the current package README.

## Deprecation metadata for maintainers

Prepare package-level Legacy metadata for the two superseded IDs, retaining every published version and existing API. The proposed alternate package range is `[1.5.59,)`, matching the version used for the source-migration checks:

| Package | Alternate package | Proposed message |
| --- | --- | --- |
| Akka.Streams.Azure.EventHub.V5 | Akka.Streams.Azure.EventHub | This interim package has been superseded by Akka.Streams.Azure.EventHub. The source namespace is unchanged, but the assembly identity changes; replace the package reference, rebuild and test checkpoint behavior. See the migration guide. |
| Akka.Streams.SignalR.AspNetCore | Akka.Streams.SignalR | This package has been superseded by Akka.Streams.SignalR. Update namespace imports and rebuild. The current package targets net6.0; netcoreapp3.1 applications require a framework migration. See the migration guide. |

Publish through the package owner's NuGet deprecation controls, then verify the registration metadata for the selected versions. Record that publication separately from a documentation merge. Do not unlist/delete packages or add compiler-level obsolete errors as part of this metadata operation. Channels' inspected latest version already has Legacy metadata, so no duplicate publication is needed for it.
