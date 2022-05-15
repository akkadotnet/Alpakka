# File

The File connectors provide additional connectors for filesystems complementing the sources and sinks for files already included in core Akka Streams.

## Listening to changes in a directory

The `DirectoryChangesSource` will emit elements every time there is a change to a watched directory
in the local filesystem, the emitted change consists of the path that was changed and an enumeration
describing what kind of change it was.

In this sample we simply print each change to the directory to standard output:

```csharp
const int maxBufferSize = 1000;
var tempDir = Path.GetTempPath();

DirectoryChangesSource.Create(tempDir, maxBufferSize)
    .RunForeach(e =>
    {
        var (changedPath, change) = e;
        Console.WriteLine($"Path: {changedPath}, Change: {change}");
    }, _materializer);

```

## Rotating the file to stream into 

The `LogRotatorSink` will create and write to multiple files. This sink takes a creator as parameter which returns a `Func<ByteString, Option<string>>`. If the generated function returns a path the sink will rotate the file output to this new path and the actual `ByteString` will be written to this new file too. With this approach the user can define a custom stateful file generation implementation.

### Example: size-based rotation

```csharp
const int max = 10 * 1024 * 1024;
var size = (long)max;

Option<string> FileSizeTriggerCreator(ByteString element)
{
    if (size + element.Count > max)
    {
        var path = Path.GetTempFileName();
        size = element.Count;
        return path;
    }

    size += element.Count;
    return Option<string>.None;
}

var sizeRotatorSink = LogRotatorSink.Create(FileSizeTriggerCreator);
```

### Example: time-based rotation

```csharp
var destinationDir = Path.GetTempPath();
var currentFilename = Option<string>.None;

Option<string> TimeBasedTriggerCreator(ByteString element)
{
    var newName = $"stream-{DateTime.UtcNow:yyyy-MM-dd_HH}.log";
    if (currentFilename.HasValue && currentFilename.Value.Contains(newName))
    {
        return Option<string>.None;
    }

    currentFilename = newName;
    return new Option<string>(Path.Combine(destinationDir, newName));
}

var timeBasedRotatorSink = LogRotatorSink.Create(TimeBasedTriggerCreator);
```

