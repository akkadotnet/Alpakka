# Akka.Streams.Azure.ServiceBus Simple Producer and Consumer Example

This folder contains an example of how a simple akka stream produces messages over to azure service bus and another simple akka stream consumes them.

From the repository root, run:

```shell
aspire start --apphost src/Azure/Examples/Simple/ServiceBus/ServiceBus.AppHost/ServiceBus.AppHost.csproj
```

Alternatively, run the `ServiceBus.AppHost: http` profile in your IDE. You need the pinned .NET SDK, Aspire CLI, and Docker running Linux containers. Aspire starts the local Service Bus emulator and its SQL Server companion; no Azure account is required.

The sample publishes to the `orders` topic and reads from `shipping-subscriber`. Automated tests launch this AppHost with `--Testing=true`, which disables the producer and consumer and declares an isolated test queue. See the [package README](https://github.com/akkadotnet/Alpakka/blob/dev/src/Azure/Akka.Streams.Azure.ServiceBus/README.md) for API behavior and test commands.
