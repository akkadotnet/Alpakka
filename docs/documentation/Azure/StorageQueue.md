# Azure Queue Storage

Queue Storage sources and sinks using a QueueClient supplied by the application. Applications explicitly delete received messages after processing.

Install [Akka.Streams.Azure.StorageQueue](https://www.nuget.org/packages/Akka.Streams.Azure.StorageQueue). The [package README](https://github.com/akkadotnet/Alpakka/blob/dev/src/Azure/Akka.Streams.Azure.StorageQueue/README.md) contains the maintained usage examples, materialized values, defaults, ownership rules and known limits. Packages built from current source include that README; older published releases may omit it.

See [local test instructions](https://github.com/akkadotnet/Alpakka/blob/dev/docs/community/Testing.md) for the service boundary and platform coverage.
