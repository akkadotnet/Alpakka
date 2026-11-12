# Legacy package migrations

Package names and assembly identities matter during migration. Rebuild and test an application after replacing a package; do not replace a DLL with a differently named assembly and expect an unchanged executable to work. Published legacy artifacts remain available.

## Package status

NuGet registration metadata was checked on 12 November 2026, including every published version of these three legacy IDs. The latest stable versions are summarized below.

| Legacy package | Latest legacy version | Current location | Migration requirements |
| --- | --- | --- | --- |
| [Akka.Streams.Channels](https://www.nuget.org/packages/Akka.Streams.Channels/1.5.37) | 1.5.37; already marked Legacy, with Akka.Streams as the alternate | Akka.Streams | Replace factory calls and remove the old namespace/package after rebuilding; see [Channels](Channels.md). |
| [Akka.Streams.Azure.EventHub.V5](https://www.nuget.org/packages/Akka.Streams.Azure.EventHub.V5/1.5.28) | 1.5.28; no deprecation metadata was present | Akka.Streams.Azure.EventHub | The source namespace stays Akka.Streams.Azure.EventHub; the assembly identity changes. |
| [Akka.Streams.SignalR.AspNetCore](https://www.nuget.org/packages/Akka.Streams.SignalR.AspNetCore/1.5.15) | 1.5.15; no deprecation metadata was present | Akka.Streams.SignalR | Update namespace imports, rebuild, and check the host framework requirement. |

The last two package IDs are superseded names. This documentation does not claim that their NuGet deprecation metadata has already been published.

Checking only the latest version misses existing users: Channels has 23 published versions, but only six have Legacy metadata. Its other 17 versions, all 12 EventHub.V5 versions and all 19 SignalR.AspNetCore versions lack that metadata. All 54 versions are still listed. The publication work therefore covers 48 versions, while preserving the six existing Channels entries.

The migration examples below were executed against the latest legacy releases in the table, not every historical beta. Older releases can require additional source, SDK or host changes. Deprecation identifies a retired package ID; it does not promise a binary-compatible replacement or identical migration steps for every version.

## EventHub.V5

The current [Event Hubs package](Azure/EventHub.md) uses Azure.Messaging.EventHubs. EventHub.V5 was an interim package name and used the same public `Akka.Streams.Azure.EventHub` namespace. Its published 1.5.28 package and the current adapter both provide .NET Standard 2.0 and .NET 6.0 assets.

Replace the `Akka.Streams.Azure.EventHub.V5` package reference with `Akka.Streams.Azure.EventHub`, remove any explicit references to the old assembly, and rebuild all consumers. Factory construction and context/checkpoint delegate examples were compiled and executed against both legacy 1.5.28 and current 1.5.59 packages without changing that source. These checks did not contact Azure; native delivery/checkpoint coverage belongs to the current connector's integration suite.

This is separate from migrating an application that still uses Microsoft.Azure.EventHubs, EventProcessorHost or the old IEventProcessor API. Those SDK APIs require an explicit migration to Azure.Messaging.EventHubs and its processor/checkpoint model. Follow the current package README rather than the historical EventProcessorHost examples.

## SignalR.AspNetCore

Use the current [ASP.NET Core SignalR package](SignalR.md). Update `using Akka.Streams.SignalR.AspNetCore` to `using Akka.Streams.SignalR`, including `.Internals` imports where they were used. Replace the package reference and rebuild; the assembly identity changes too.

The old 1.5.15 package supplied netcoreapp3.1 and net6.0 assets; 1.0.0-beta1 through 1.0.0-beta5 instead supplied .NET Standard 2.0. The current package supplies net6.0. A host that cannot consume net6.0 assets needs a framework migration before replacing the package. This includes netcoreapp3.1 applications and earlier hosts that consumed the old .NET Standard asset; there is no drop-in current asset for those targets.

Settings, events, routing, DI and hub/connector factory examples were compiled and executed with the old package and, after the namespace change, current 1.5.59. Those examples did not open network connections. The current connector's separate WebSocket integration tests cover transport behavior.

The OWIN `PersistentConnection`/`StreamConnection` examples describe an older SignalR integration. They are not APIs of the current typed-hub adapter. Use the complete server/client examples in the current package README.

## Deprecation metadata for maintainers

Prepare package-level Legacy metadata for the missing versions below, retaining every published version and existing API. Keep the six existing Channels entries unchanged. The proposed range for the two renamed packages is `[1.5.59,)`, matching the version used for the source-migration checks; Channels retains its existing alternate-package policy.

| Package | Versions needing metadata | Alternate package | Alternate range |
| --- | --- | --- | --- |
| Akka.Streams.Channels | 17: 1.0.0-beta1 through beta9; 1.0.1-beta1 through beta4; 1.4.49, 1.5.0, 1.5.0.1 and 1.5.7 | Akka.Streams | `*`, matching its six existing entries |
| Akka.Streams.Azure.EventHub.V5 | All 12 published versions, through 1.5.28 | Akka.Streams.Azure.EventHub | `[1.5.59,)` |
| Akka.Streams.SignalR.AspNetCore | All 19 published versions, through 1.5.15 | Akka.Streams.SignalR | `[1.5.59,)` |

The Channels `*` range preserves its existing recommendation policy; it is not a claim that every Akka.Streams version contains these factories or preserves every application contract. Its source-migration checks used Akka.Streams 1.5.60. Select only the 17 missing Channels versions: applying a new message to all versions would overwrite the existing records, including a distinct message on 1.5.8.

Use the Legacy reason and the package-specific message:

| Package | Alternate package | Proposed message |
| --- | --- | --- |
| Akka.Streams.Channels | Akka.Streams | This functionality is included in Akka.Streams. This package is no longer maintained. Replace the factories, rebuild and test the application; review the Channels migration guide, including its reader-completion limitation. |
| Akka.Streams.Azure.EventHub.V5 | Akka.Streams.Azure.EventHub | This interim package has been superseded by Akka.Streams.Azure.EventHub. Replace the package reference, review namespace/API requirements, rebuild and test checkpoint behavior. The assembly identity changes. See the migration guide. |
| Akka.Streams.SignalR.AspNetCore | Akka.Streams.SignalR | This package has been superseded by Akka.Streams.SignalR. Review namespace imports and rebuild. The current package supplies net6.0 assets only; hosts that cannot consume them need a framework migration, including older hosts using the legacy .NET Standard asset. See the migration guide. |

Publish after this migration guide is available at its canonical documentation URL. An authenticated package owner can use NuGet.org's Manage packages → Deprecation controls, select the versions above, and enter the Legacy reason, alternate ID/range and message. NuGet's [deprecation workflow](https://learn.microsoft.com/en-us/nuget/nuget-org/deprecate-packages) documents these controls. Its custom message appears on NuGet.org; clients do not display that message, so it cannot substitute for the correct alternate ID and reason.

Verify the result through the [NuGet registration resource](https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource), discovering its URL from the service index and following every registration page. Require Legacy metadata and the intended alternate ID/range on all 48 changed versions, unchanged metadata on the six existing Channels versions, and all 54 versions still listed. Record that publication separately from a documentation merge. Do not unlist/delete packages or add compiler-level obsolete errors as part of this metadata operation.
