# Akka.Streams.File

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.File.svg)](https://www.nuget.org/packages/Akka.Streams.File/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.File.svg)](https://www.nuget.org/packages/Akka.Streams.File/)

Additional filesystem connectors for Akka.NET Streams: directory change notifications and a sink that rotates between files or application-supplied sinks. The package targets .NET Standard 2.0 and .NET 6. Ordinary file reads and writes are already available through `FileIO` in Akka.Streams.

```sh
dotnet add package Akka.Streams.File
```

## Rotate output files

`LogRotatorSink.Create` consumes `ByteString` values and materializes `Task<Done>`. Its trigger runs for each element. Returning a path opens a new file sink and writes that element into the new file; returning `Option<string>.None` keeps writing to the current file. **Elements received before the first path are discarded.** The trigger must return a path for the first element you want to retain.

This complete example writes two elements per file. Choose a writable `LOG_DIRECTORY` or use the default `logs` subdirectory. The application creates the directory; the connector does not create missing parent directories.

```csharp
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.File;
using Akka.Util;

var directory = Environment.GetEnvironmentVariable("LOG_DIRECTORY") ?? Path.Combine(Environment.CurrentDirectory, "logs");
Directory.CreateDirectory(directory);
var run = Guid.NewGuid().ToString("N");
var index = 0;
Option<string> Rotate(ByteString element)
{
    var current = index++;
    return current % 2 == 0
        ? Option<string>.Create(Path.Combine(directory, $"{run}-{current / 2}.log"))
        : Option<string>.None;
}
var system = ActorSystem.Create("rotating-files");
try
{
    await Source.From(new[] { "first\n", "café\n", "last\n" })
        .Select(ByteString.FromString)
        .RunWith(LogRotatorSink.Create(Rotate, FileMode.Create), system.Materializer());
}
finally { await system.Terminate(); }
```

The default mode is `FileMode.OpenOrCreate`, which writes from the beginning without truncating an existing longer file. Choose `FileMode.Create` to truncate or `FileMode.Append` to append. Bytes are written as supplied; the connector adds no encoding conversion, separators or newline. Rotation is triggered by incoming elements, so a time-based trigger does not rotate an idle stream. It cannot split a single element at a size boundary.

Completion waits for all opened sink materialized tasks. A child failure, cancellation, trigger exception or factory exception fails the outer task; materializer shutdown also fails a pending task. Successful early completion of the current sink cancels upstream and still awaits older sinks. Rotated older sinks can finish independently without stopping the current sink. Failure is not an atomic rollback: earlier writes can remain on disk. Completion/closed handles do not establish durable storage after power loss.

`WithSinkFactory<TCriterion, TResult>` uses `ByteString` input and creates `Sink<ByteString, Task<TResult>>` children. `WithTypedSinkFactory<T, TCriterion, TResult>` accepts other element types. Their triggers return `Option<TCriterion>` rotation criteria; the outer materialized value remains `Task<Done>`. Supply fresh trigger state for each independent stream: the connector retains the supplied delegate rather than cloning its captured mutable state.

## Watch a directory

`DirectoryChangesSource.Create(path, maxBufferSize)` emits `(string path, DirectoryChange change)` values and materializes `NotUsed`. The public `DirectoryChange` enum is in `Akka.Streams.File.Impl`. The watched directory must already exist; a missing directory throws during stream materialization. This example runs until Ctrl+C and waits for stream termination before terminating the actor system.

```csharp
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.File;
using Akka.Streams.File.Impl;

var directory = Environment.GetEnvironmentVariable("WATCH_DIRECTORY") ?? Environment.CurrentDirectory;
var system = ActorSystem.Create("directory-watcher");
var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; stopped.TrySetResult(true); };
Console.CancelKeyPress += handler;
try
{
    var (kill, completion) = DirectoryChangesSource.Create(directory, 1024)
        .ViaMaterialized(KillSwitches.Single<(string, DirectoryChange)>(), Keep.Right)
        .ToMaterialized(Sink.ForEach<(string, DirectoryChange)>(change => Console.WriteLine($"{change.Item2}: {change.Item1}")), Keep.Both)
        .Run(system.Materializer());
    await Task.WhenAny(stopped.Task, completion);
    kill.Shutdown();
    await completion;
}
finally
{
    Console.CancelKeyPress -= handler;
    await system.Terminate();
}
```

The source uses `FileSystemWatcher`, watches only the immediate directory, and maps create/delete events to `Creation`/`Deletion`; writes and renames map to `Modification`. Rename events report the new full path, with no old-path field. It does not enumerate existing files, recurse into subdirectories or provide a durable event log. Operating systems can duplicate, coalesce or lose notifications; do not depend on exactly one event per operation.

The managed event queue fails with `BufferOverflowException` when it exceeds `maxBufferSize`. Backpressure controls emission but cannot pause filesystem changes. The underlying watcher's separate native buffer can overflow too; its `Error` event is not currently forwarded by this adapter. Use reconciliation or another durable mechanism when missed events are unacceptable. Cancellation stops the source and disposes its owned watcher. There is no checkpoint/replay or automatic watch recovery.

## Tests

These connectors use the real temporary filesystem; they do not require Docker, Aspire or cloud credentials. From the repository root, run Linux tests with:

```sh
dotnet test src/File/Akka.Streams.File.Tests/Akka.Streams.File.Tests.csproj -c Release --framework net10.0
```

On Windows, run the portable suite on .NET Framework 4.8:

```sh
dotnet test src/File/Akka.Streams.File.Tests/Akka.Streams.File.Tests.csproj -c Release --framework net48 --filter "Platform!=Linux"
```

Tests retain the original watcher/rotation cases and add exact file-mode/UTF-8 output, exclusive reopening, rename/cancellation, missing directories and controlled child lifecycle failures. Linux additionally observes the target directory's actual inotify watch before cancellation and its removal afterward, using `stat` and `/proc/self/fdinfo`. That resource check is explicitly excluded from Windows; ordinary directory deletion alone is not proof of native watch release.
