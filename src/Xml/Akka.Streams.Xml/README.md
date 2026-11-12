# Akka.Streams.Xml

[![NuGet version](https://img.shields.io/nuget/v/Akka.Streams.Xml.svg)](https://www.nuget.org/packages/Akka.Streams.Xml/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Akka.Streams.Xml.svg)](https://www.nuget.org/packages/Akka.Streams.Xml/)

XML event parsing, adjacent-text coalescing and local-name path filtering for Akka.NET Streams. The package targets .NET Standard 2.0 and .NET 6. All three DSL flows materialize `NotUsed`; retain a terminal sink's task to observe completion or failure.

The current parser has an important input limitation: its `XmlReader` feeder can fail when a text/XML token is split between incoming byte chunks. The examples supply a complete, small UTF-8 fragment in one `ByteString` that fits the configured buffer. General arbitrary byte-stream parsing needs a separately reviewed parser improvement; a larger buffer alone is not a guarantee of arbitrary chunk support.

```sh
dotnet add package Akka.Streams.Xml
```

## Parse a bounded XML fragment

`XmlParsing.Parser(bufferSize: 1024)` consumes `ByteString` values and emits `IParseEvent` values. The minimum feeder buffer size is 64 bytes; constructing a parser with a smaller value throws `ArgumentException`.

This complete example prints event kinds and content, including balanced start/end events for a self-closing item:

```csharp
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Xml;
using Akka.Streams.Xml.Dsl;

var payload = ByteString.FromString("<root><item id='1'>café<![CDATA[ & more]]></item><item id='2'/></root>");
var system = ActorSystem.Create("xml-events", "akka.loglevel = WARNING");
try
{
    var events = await Source.Single(payload).Via(XmlParsing.Parser())
        .RunWith(Sink.Seq<IParseEvent>(), system.Materializer());
    foreach (var value in events)
    {
        var line = value switch
        {
            StartElement element => $"start:{element.LocalName}",
            EndElement element => $"end:{element.LocalName}",
            Characters text => $"text:{text.Text}",
            CData text => $"cdata:{text.Text}",
            Comment comment => $"comment:{comment.Text}",
            ProcessingInstruction instruction => $"pi:{instruction.Target} {instruction.Data}",
            _ => value.GetType().Name
        };
        Console.WriteLine(line);
    }
}
finally { await system.Terminate(); }
```

Events include `StartDocument`, `EndDocument`, `StartElement`, `EndElement`, `Characters`, `CData`, `Comment` and `ProcessingInstruction`. `StartElement.Attributes` is an immutable dictionary. The parser uses fragment conformance and can emit document boundaries for successive root fragments; those markers are adapter events rather than a lossless representation of XML declarations. Final events drain under demand and completion does not require a further request solely to discover EOF.

Names use `XmlReader.LocalName`: namespace URIs/prefixes are not preserved in element or attribute names. Attributes with different prefixes but the same local name can collide and fail with `ArgumentException`. Ignorable whitespace is suppressed, and other unhandled whitespace nodes are not represented. This event model is not a lossless XML roundtrip format.

Malformed XML and DTD input fail the tested parser path with `XmlException`. The stage owns its reader and memory feeder and disposes them when it stops. Cancellation propagates upstream; byte input already received may have been buffered or read ahead.

## Coalesce text and select element contents

`XmlParsing.Coalesce(maximumTextLength)` joins consecutive `Characters` and `CData` events into one `Characters` event. Its maximum counts UTF-16 string code units across the consecutive text, not the number of events or input bytes. Exceeding it fails with `IllegalStateException`; it also flushes a final text sequence when upstream completes.

`XmlParsing.Subslice(path)` matches an exact, case-sensitive sequence of local element names from the root and emits the contents of each matching element, including nested descendants. It excludes the matched element's own opening/closing events. It is not XPath and does not match namespace URIs. An empty or null path passes events through.

This complete example coalesces the two text kinds and selects the contents of each `root/item`, printing `café & more`:

```csharp
using System.Collections.Immutable;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Xml;
using Akka.Streams.Xml.Dsl;

var payload = ByteString.FromString("<root><item id='1'>café<![CDATA[ & more]]></item><item id='2'/></root>");
var system = ActorSystem.Create("xml-selection", "akka.loglevel = WARNING");
try
{
    await Source.Single(payload)
        .Via(XmlParsing.Parser())
        .Via(XmlParsing.Coalesce(64))
        .Via(XmlParsing.Subslice(ImmutableList.Create("root", "item")))
        .Where(value => value is Characters)
        .Select(value => ((Characters)value).Text)
        .RunWith(Sink.ForEach<string>(Console.WriteLine), system.Materializer());
}
finally { await system.Terminate(); }
```

`bufferSize` controls the feeder allocation, not a maximum document, incoming chunk or total memory limit. Overflow bytes and the underlying reader can retain additional data. Coalescing limits adjacent emitted text only; it does not impose parser-wide document/entity/attribute limits. The package provides no schema validation, checkpoint/replay, automatic error recovery or universal untrusted-input resource guarantee.

The public `StreamingXmlParser`, `Coalesce`, `Subslice` and event classes remain available for compatibility; the DSL wraps those stages. The documented arbitrary-token chunking limitation, namespace information loss and broader parser redesign remain separate library work.

## Tests

From the repository root:

```sh
dotnet test src/Xml/Akka.Streams.Xml.Tests/Akka.Streams.Xml.Tests.csproj -c Release --framework net10.0
# On Windows with .NET Framework 4.8:
dotnet test src/Xml/Akka.Streams.Xml.Tests/Akka.Streams.Xml.Tests.csproj -c Release --framework net48
```

Tests retain parsing, CDATA, path selection and text-limit assertions. Previously skipped coalescer cases now use real text/CData event streams, and a separate executed case characterizes unsupported parser text chunking. Added contracts cover self-closing roots/children/repeated fragments, completion with exact demand, trailing comments, malformed XML/DTD failures, UTF-8/local namespace names and cancellation. No Docker, Aspire host or cloud credentials are required.
