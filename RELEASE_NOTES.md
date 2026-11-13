#### 1.5.74 (Unreleased) ####

* Bump the minimum Akka.NET dependency to [1.5.74](https://github.com/akkadotnet/akka.net/releases/tag/1.5.74), with aligned Streams and TestKit dependencies.
* Include an individual README in each of the twelve connector packages, with usage, ownership, delivery semantics, limitations and linked NuGet version/download badges. Expand the repository README and documentation navigation.
* Update compatible Azure, AWS, AMQP and SignalR dependencies. Package READMEs document remaining SDK support and version limitations.
* Azure Service Bus JSON sources await successful message completion before emitting values and propagate settlement failures.
* Fix SQS batch-response correlation when response entries arrive in a different order.
* Improve SNS and Kinesis completion, cancellation and failure handling.
* Remove blocking SignalR send waits while preserving sequential delivery and existing materialized values.
* Propagate AMQP conversion and connection failures through the materialized completion task.
* Correct CSV parsing across split CRLF/escape sequences and XML parser empty-element/document completion, including reader and feeder cleanup.
* Improve rotating-file sink cleanup on completion and failures.
* Add required service integration, package/API, unchanged-consumer and packaged-example validation. Harden Event Hubs startup and Service Bus polling tests.
* Replace legacy FluentAssertions usages with xUnit assertions, retaining their payload, ordering, exception and deadline checks.

**Upstream compatibility:** Alpakka retains its public APIs and library targets. Raising the Akka.NET minimum from 1.5.60 inherits upstream binary changes: the old `RetrySupport.Retry` overload with an `int randomFactor` is replaced by a `double` overload, and `ISourceQueue<T>` requires `OfferAsync(T, CancellationToken)`. Applications using that old compiled overload or custom queue implementations need to update and rebuild against the new dependencies.

#### 1.5.59 January 26th 2026 ####

* Bumped to [Akka.NET v1.5.59](https://github.com/akkadotnet/akka.net/releases/tag/1.5.59)

#### 1.5.57 December 23rd 2025 ####

* Bumped to [Akka.NET v1.5.57](https://github.com/akkadotnet/akka.net/releases/tag/1.5.57)

#### 1.5.40 April 18th 2025 ####

* Bumped to [Akka.NET v1.5.40](https://github.com/akkadotnet/akka.net/releases/tag/1.5.40)
* Resolved: [Akka.Streams.Azure.EventHubs: Source no longer emits empty event](https://github.com/akkadotnet/Alpakka/issues/1979)
* Akka.Streams.SignalR: migrated away from [`Microsoft.AspNetCore.App`](https://www.nuget.org/packages/Microsoft.AspNetCore.App/), which was for `netcoreapp3.1` and earlier, to [`Microsoft.AspNetCore.SignalR`](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR), which supports all newer versions of ASP.NET Core and SignalR.
