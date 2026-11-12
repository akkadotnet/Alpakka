# Akka Streams connectors for Azure

- [Azure Queue Storage](Akka.Streams.Azure.StorageQueue/README.md): caller-owned QueueClient, string publishing and explicit deletion after processing.
- [Azure Event Hubs](Akka.Streams.Azure.EventHub/README.md): the current Azure.Messaging.EventHubs adapter, including standard/batched processing and checkpoints.
- [Azure Service Bus](Akka.Streams.Azure.ServiceBus/README.md): queue/topic sources, sinks and settlement semantics.

Use the current `Akka.Streams.Azure.EventHub` package for the Azure.Messaging.EventHubs SDK. `Akka.Streams.Azure.EventHub.V5` was an interim package name. Its public namespace was already `Akka.Streams.Azure.EventHub`; migrating to the current package requires replacing the package reference and rebuilding because the assembly identity changes. This is distinct from migrating an application that still uses the older Microsoft.Azure.EventHubs SDK.

See [legacy package migrations](../../docs/documentation/LegacyPackages.md) for the package/namespace/target requirements. Published legacy artifacts remain available.
