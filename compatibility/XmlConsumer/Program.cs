using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Xml;
using Akka.Streams.Xml.Dsl;

internal static class Program
{
    private static async Task Main()
    {
        var system = ActorSystem.Create("old-xml-consumer");
        try
        {
            Flow<ByteString, IParseEvent, NotUsed> parse = XmlParsing.Parser();
            Flow<IParseEvent, IParseEvent, NotUsed> coalesce = XmlParsing.Coalesce(64);
            Flow<IParseEvent, IParseEvent, NotUsed> select = XmlParsing.Subslice(ImmutableList.Create("root", "item"));
            var payload = ByteString.FromString("<root><item key='value'>café<![CDATA[ & more]]></item></root>");
            var events = await Source.Single(payload).Via(parse).RunWith(Sink.Seq<IParseEvent>(), system.Materializer());
            if (events.Count != 8 || !(events[0] is StartDocument) || !(events[7] is EndDocument)
                || events.OfType<StartElement>().Single(element => element.LocalName == "item").Attributes["key"] != "value"
                || events.OfType<Characters>().Single().Text != "café" || events.OfType<CData>().Single().Text != " & more")
                throw new InvalidOperationException("Retained XML event shape or text changed.");
            var contents = await Source.Single(payload).Via(parse).Via(coalesce).Via(select)
                .RunWith(Sink.Seq<IParseEvent>(), system.Materializer());
            if (contents.Count != 1 || !(contents[0] is Characters text) || text.Text != "café & more")
                throw new InvalidOperationException("Retained coalescing/path behavior changed.");
            var rawParser = new StreamingXmlParser(1024);
            var rawCoalesce = new Coalesce(64);
            var rawSubslice = new Subslice(ImmutableList<string>.Empty);
            var instruction = new ProcessingInstruction("target", "data");
            var comment = new Comment("comment");
            if (rawParser.Shape == null || rawCoalesce.MaximumTextLength != 64 || rawSubslice.Path.Count != 0
                || instruction.Target != "target" || instruction.Data != "data" || comment.Text != "comment")
                throw new InvalidOperationException("Retained public stages/events changed.");
            Console.WriteLine("Previously compiled XML consumer passed (events, attributes, UTF-8, CDATA and path selection).");
        }
        finally { await system.Terminate(); }
    }
}
