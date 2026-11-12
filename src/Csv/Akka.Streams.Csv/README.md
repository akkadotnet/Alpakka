# Akka.Streams.Csv

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Csv.svg)](https://www.nuget.org/packages/Akka.Streams.Csv/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Csv.svg)](https://www.nuget.org/packages/Akka.Streams.Csv/)

CSV parsing, formatting and header-to-dictionary flows for Akka.NET Streams. The package targets .NET Standard 2.0 and .NET 6. It processes stream elements in the application process; no broker, Aspire host or container is required.

```sh
dotnet add package Akka.Streams.Csv
```

## Write CSV

`CsvFormatting.Format` accepts `ImmutableList<string>` rows and emits `ByteString` chunks. By default it uses UTF-8, comma delimiters, double quotes, backslash escaping, CRLF line endings and `CsvQuotingStyle.Required`. A field is quoted when needed for delimiters, quotes, escapes or embedded newlines; `Always` quotes every non-null field. Quotes and escape characters are doubled when formatting quoted fields. No byte-order mark is added unless supplied explicitly.

This complete example writes a header and two rows. Set `CSV_OUTPUT` to an output path or use `example.csv` in the working directory. It uses `FileMode.Create`, so an existing file at that path is overwritten.

```csharp
using System.Collections.Immutable;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;

var path = Environment.GetEnvironmentVariable("CSV_OUTPUT") ?? "example.csv";
var system = ActorSystem.Create("csv-writer", "akka.loglevel = WARNING");
try
{
    var rows = new[]
    {
        ImmutableList.Create("name", "note"),
        ImmutableList.Create("café", "comma, quote \" and newline\ninside"),
        ImmutableList.Create("last", "plain")
    };
    var result = await Source.From(rows)
        .Via(CsvFormatting.Format(byteOrderMark: ByteOrderMark.UTF8))
        .RunWith(FileIO.ToFile(new FileInfo(path), FileMode.Create), system.Materializer());
    if (!result.WasSuccessful) throw result.Error;
    Console.WriteLine($"Wrote {result.Count} bytes to {path}");
}
finally { await system.Terminate(); }
```

A configured BOM is emitted as its own initial chunk, including for an empty upstream. Each row is formatted independently. Empty rows produce the configured line ending; null fields become empty columns. The current empty-row fast path encodes its line ending with UTF-8 even when a different encoding is selected, so use UTF-8 for consistent empty/nonempty output. Non-UTF-8 output and its chosen BOM are not necessarily supported by this package's byte-oriented parser.

## Read CSV into dictionaries

`CsvParsing.LineScanner` accepts arbitrary `ByteString` chunks and emits `ImmutableList<ByteString>` rows. `CsvToDictionary.ToDictionary` consumes the first row as header names, then emits `Dictionary<string, ByteString>` values for later rows. Header text uses UTF-8 unless an encoding is supplied; field values remain bytes until decoded by the application.

Run this example against the file above, or set `CSV_INPUT` to a compatible CSV file with `name` and `note` headers.

```csharp
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;

var path = Environment.GetEnvironmentVariable("CSV_INPUT") ?? "example.csv";
var system = ActorSystem.Create("csv-reader", "akka.loglevel = WARNING");
try
{
    var (read, completion) = FileIO.FromFile(new FileInfo(path))
        .Via(CsvParsing.LineScanner())
        .Via(CsvToDictionary.ToDictionary())
        .ToMaterialized(Sink.ForEach<Dictionary<string, ByteString>>(row =>
            Console.WriteLine($"{row["name"].ToString(Encoding.UTF8)}: {row["note"].ToString(Encoding.UTF8)}")), Keep.Both)
        .Run(system.Materializer());
    await completion;
    var result = await read;
    if (!result.WasSuccessful) throw result.Error;
}
finally { await system.Terminate(); }
```

All three CSV DSL flows materialize `NotUsed`; the examples retain completion from their file or terminal sink. Cancellation propagates to upstream. The flows create per-materialization parsing/header state and do not own a separate external client.

## Parsing rules and limits

Parsing defaults to comma, double quote and backslash; delimiters and quote/escape characters are single bytes. LF, CR and CRLF terminate records outside quotes. Quoted fields can contain those bytes as data. CRLF remains one terminator when split between chunks; ordinary blank records remain a single empty field. A UTF-8 BOM at the start is consumed. Recognized UTF-16/UTF-32 BOMs throw `UnsupportedCharsetException`; Unicode U+2028 remains field data rather than a record separator.

Split escape sequences wait for their following byte in the streaming flow. Invalid escapes and dangling escapes at stream EOF fail with `MalformedCsvException`. A final unquoted row without a newline is emitted at EOF. Malformed-quote handling is not universally strict: an unterminated quoted final record can be left un-emitted without a failure. Validate input separately if every malformed record must be rejected.

The parser buffers incomplete rows and has no public maximum record/field length setting. Backpressure controls emitted rows but does not bound memory for a single arbitrarily long incomplete record. Field bytes are not a UTF-8 validation guarantee. No schema validation, typed conversion, checkpointing or recovery after a parse error is provided.

`CsvToDictionary.WithHeaders(ImmutableList<string>)` uses supplied headers and treats every incoming row as data. Header/data length differences use only the shared prefix: excess data is discarded and missing columns are omitted. Duplicate header names that collide in a row fail with the dictionary's `ArgumentException`; header-only input emits no dictionary. Choose a separate validation stage for strict column counts or schemas.

The low-level public `CsvParser` and `CsvFormatter` helpers are retained for compatibility; prefer the DSL for streaming. The public three-argument `CsvParser` retains its immediate incomplete-escape validation when `Poll(true)` is called directly. Only the streaming stage opts into waiting for a subsequent chunk; `Poll(false)` still rejects dangling escapes.

## Tests

From the repository root, run either retained test target:

```sh
dotnet test src/Csv/Akka.Streams.Csv.Tests/Akka.Streams.Csv.Tests.csproj -c Release --framework net10.0
# On Windows with .NET Framework 4.8:
dotnet test src/Csv/Akka.Streams.Csv.Tests/Akka.Streams.Csv.Tests.csproj -c Release --framework net48
```

Tests retain parser/formatter/dictionary assertions and real CSV fixtures. Additional streaming cases cover UTF-8/BOM/quote boundaries, split CRLF and escapes, invalid escapes/EOF errors, standalone CR, preserved blank records, final demand/completion and cancellation. They require no Docker or cloud credentials.
