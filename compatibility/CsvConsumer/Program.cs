using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Csv;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;

internal static class Program
{
    private static async Task Main()
    {
        var system = ActorSystem.Create("old-csv-consumer");
        try
        {
            Flow<ImmutableList<string>, ByteString, NotUsed> format = CsvFormatting.Format();
            Flow<ByteString, ImmutableList<ByteString>, NotUsed> parse = CsvParsing.LineScanner();
            Flow<ImmutableList<ByteString>, Dictionary<string, ByteString>, NotUsed> dictionary = CsvToDictionary.ToDictionary();
            var rows = new[] { ImmutableList.Create("name", "value"), ImmutableList.Create("café", "x,y") };
            var formatted = await Source.From(rows).Via(format)
                .RunWith(Sink.Aggregate(ByteString.Empty, (ByteString all, ByteString chunk) => all + chunk), system.Materializer());
            if (formatted.ToString(Encoding.UTF8) != "name,value\r\ncafé,\"x,y\"\r\n")
                throw new InvalidOperationException("Retained formatting bytes/defaults changed.");
            var parsed = await Source.Single(formatted).Via(parse).Via(dictionary)
                .RunWith(Sink.Seq<Dictionary<string, ByteString>>(), system.Materializer());
            if (parsed.Count != 1 || parsed[0]["name"].ToString(Encoding.UTF8) != "café"
                || parsed[0]["value"].ToString(Encoding.UTF8) != "x,y")
                throw new InvalidOperationException("Retained header mapping or payload changed.");
            var direct = new CsvParser(CsvParsing.Comma, CsvParsing.DoubleQuote, CsvParsing.Backslash);
            direct.Offer(ByteString.FromString("a\\"));
            var rejectedEscape = false;
            try { direct.Poll(true); }
            catch (MalformedCsvException) { rejectedEscape = true; }
            if (!rejectedEscape)
                throw new InvalidOperationException("Retained public-parser immediate escape validation changed.");
            var lowLevelFormatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            if (lowLevelFormatter.ToCsv(new[] { "a", "b" }).ToString() != "a,b\r\n"
                || ByteOrderMark.UTF8.Count != 3 || ByteOrderMark.None.Count != 0
                || CsvToDictionary.WithHeaders(ImmutableList.Create("header")) is null)
                throw new InvalidOperationException("Retained public helpers changed.");
            Console.WriteLine("Previously compiled CSV consumer passed (exact UTF-8 formatting, parsing, headers and public escape errors).");
        }
        finally { await system.Terminate(); }
    }
}
