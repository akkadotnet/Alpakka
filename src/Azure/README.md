# Akka Streams Plug-ins For Azure

## Akka Streams For Azure EventHubs

> __WARNING__
>
> As of February 2020, the `Microsoft.Azure.EventHubs` package is deprecated in favor of the new `Azure.Messaging.EventHubs` package. Because of this, we are phasing out `Microsoft.Azure.EventHubs` support in favor of the new `Azure.Messaging.EventHubs` SDK NuGet package.
>
> We will release an interim NuGet package named `Akka.Streams.Azure.EventHub.V5` that uses the same `Akka.Streams.Azure.EventHub` namespace, this is done to preserve our package namespace in the future for easy migration. The new `Akka.Streams.Azure.EventHub.V5` package will replace the `Akka.Streams.Azure.EventHub` package on October 2022. Please migrate your package to the new `Akka.Streams.Azure.EventHub.V5` package.

There are currently two packages that you can use to connect Akka.Streams to Azure EventHubs.

1. If you are starting a new project or upgrading to the new (V5) EventHubs SDK driver, you can use the new Akka streams package that uses the `Azure.Messaging.EventHubs` NuGet package [here](./Akka.Streams.Azure.EventHub.V5).
2. If you are using the legacy (V4) EventHubs SDK driver, you can use the old Akka streams package that uses the `Microsoft.Azure.EventHubs` NuGet package [here](./Akka.Streams.Azure.EventHub).

## Akka Streams For Azure ServiceBus

## Akka Streams For Azure StorageQueue