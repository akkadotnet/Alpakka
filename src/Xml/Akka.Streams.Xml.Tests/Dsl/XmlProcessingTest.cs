using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Xml.Dsl;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Xml.Tests.Dsl
{
    public class XmlProcessingTest : XmlSpec
    {
        private readonly ActorMaterializer _materializer;
        private readonly Sink<string, Task<IImmutableList<IParseEvent>>> _parser;

        public XmlProcessingTest(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();

            _parser = Flow.Create<string>()
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .ToMaterialized(Sink.Seq<IParseEvent>(), Keep.Right);
        }

        internal static void AssertEvent(IParseEvent expected, IParseEvent actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.GetType(), actual.GetType());
            switch (expected)
            {
                case StartElement element:
                    var start = (StartElement)actual;
                    Assert.Equal(element.LocalName, start.LocalName);
                    Assert.Equal(element.Attributes.OrderBy(p => p.Key), start.Attributes.OrderBy(p => p.Key));
                    break;
                case EndElement element:
                    Assert.Equal(element.LocalName, ((EndElement)actual).LocalName);
                    break;
                case TextEvent text:
                    Assert.Equal(text.Text, ((TextEvent)actual).Text);
                    break;
                case Comment comment:
                    Assert.Equal(comment.Text, ((Comment)actual).Text);
                    break;
                case ProcessingInstruction instruction:
                    var actualInstruction = (ProcessingInstruction)actual;
                    Assert.Equal(instruction.Target, actualInstruction.Target);
                    Assert.Equal(instruction.Data, actualInstruction.Data);
                    break;
            }
        }

        [Fact]
        public async Task XmlParser_must_properly_parse_simple_XML()
        {
            var doc = "<doc><elem>elem1</elem><elem>elem2</elem></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);
            
            var result = (await CompletedWithin(fut));

            Assert.IsType<StartDocument>(result[0]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string>()), result[1]);
            AssertEvent(new StartElement("elem", new Dictionary<string, string>()), result[2]);
            AssertEvent(new Characters("elem1"), result[3]);
            AssertEvent(new EndElement("elem"), result[4]);
            AssertEvent(new StartElement("elem", new Dictionary<string, string>()), result[5]);
            AssertEvent(new Characters("elem2"), result[6]);
            AssertEvent(new EndElement("elem"), result[7]);
            AssertEvent(new EndElement("doc"), result[8]);
            Assert.IsType<EndDocument>(result[9]);
        }

        [Fact]
        public async Task XmlParser_must_properly_parse_multiple_XML_in_a_single_stream()
        {
            var doc = "<doc><elem>elem1</elem></doc>";
            var fut = Source
                .Single(doc)
                .Concat(Source.Single(doc))
                .RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));

            Assert.IsType<StartDocument>(result[0]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string>()), result[1]);
            AssertEvent(new StartElement("elem", new Dictionary<string, string>()), result[2]);
            AssertEvent(new Characters("elem1"), result[3]);
            AssertEvent(new EndElement("elem"), result[4]);
            AssertEvent(new EndElement("doc"), result[5]);
            Assert.IsType<EndDocument>(result[6]);
            Assert.IsType<StartDocument>(result[7]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string>()), result[8]);
            AssertEvent(new StartElement("elem", new Dictionary<string, string>()), result[9]);
            AssertEvent(new Characters("elem1"), result[10]);
            AssertEvent(new EndElement("elem"), result[11]);
            AssertEvent(new EndElement("doc"), result[12]);
            Assert.IsType<EndDocument>(result[13]);
        }

        [Fact]
        public async Task XmlParser_must_properly_process_a_comment()
        {
            var doc = "<doc><!--comment--></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));

            Assert.IsType<StartDocument>(result[0]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string>()), result[1]);
            AssertEvent(new Comment("comment"), result[2]);
            AssertEvent(new EndElement("doc"), result[3]);
            Assert.IsType<EndDocument>(result[4]);
        }

        [Fact]
        public async Task XmlParser_must_properly_process_parse_instructions()
        {
            var doc = "<?target content?><doc></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));

            Assert.IsType<StartDocument>(result[0]);
            AssertEvent(new ProcessingInstruction("target", "content"), result[1]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string>()), result[2]);
            AssertEvent(new EndElement("doc"), result[3]);
            Assert.IsType<EndDocument>(result[4]);
        }

        [Fact]
        public async Task XmlParser_must_properly_process_attributes()
        {
            var doc = "<doc good=\"yes\"><elem nice=\"yes\" very=\"true\">elem1</elem></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));

            Assert.IsType<StartDocument>(result[0]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string> {{"good", "yes"}}), result[1]);
            AssertEvent(new StartElement("elem", new Dictionary<string, string> {{"nice", "yes"}, {"very", "true"}}), result[2]);
            AssertEvent(new Characters("elem1"), result[3]);
            AssertEvent(new EndElement("elem"), result[4]);
            AssertEvent(new EndElement("doc"), result[5]);
            Assert.IsType<EndDocument>(result[6]);
        }

        [Fact]
        public async Task XmlParser_must_properly_process_CDATA_blocks()
        {
            var doc = "<doc><![CDATA[<not>even</valid>]]></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));

            Assert.IsType<StartDocument>(result[0]);
            AssertEvent(new StartElement("doc", new Dictionary<string, string>()), result[1]);
            AssertEvent(new CData("<not>even</valid>"), result[2]);
            AssertEvent(new EndElement("doc"), result[3]);
            Assert.IsType<EndDocument>(result[4]);
        }

        // I'm not sure what the original very convoluted code does
        [Fact]
        public async Task XmlParser_must_properly_parse_large_XML()
        {
            var elements = Enumerable.Range(0, 10).Select(i => i.ToString()).ToImmutableArray();

            var documentStream = Source
                .Single("<doc>")
                .Concat(Source.FromEnumerator(elements.Select(s => $"<elem>{s}</elem>").GetEnumerator))
                .Concat(Source.Single("</doc>"));

            var fut = documentStream
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .Where(evt => evt is Characters)
                .Select(evt => ((Characters)evt).Text)
                .RunWith(Sink.Seq<string>(), _materializer);

            Assert.Equal(elements.OrderBy(x => x), (await CompletedWithin(fut)).OrderBy(x => x));
        }

        [Fact]
        public async Task XmlParser_must_properly_parse_chunks_bigger_than_its_buffer_size()
        {
            var documentStream = Source
                .Single("<doc>")
                .Concat(Source.Single(
@"  <elem>
    <item>i1</item>
    <item>i2</item>
    <item>i3</item>
    <item>i1</item>
    <item>i2</item>
    <item>i3</item>
    <item>i1</item>
    <item>i2</item>
    <item>i3</item>
    <item>i1</item>
    <item>i2</item>
    <item>i3</item>
  </elem>"
                ))
                .Concat(Source.Single("</doc>"));

            var fut = documentStream
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser(bufferSize:64))
                .RunWith(Sink.Seq<IParseEvent>(), _materializer);

            var events = await CompletedWithin(fut);
            Assert.IsType<StartDocument>(events[0]);
            Assert.IsType<EndDocument>(events[events.Count - 1]);
            Assert.Equal(Enumerable.Repeat(new[] { "i1", "i2", "i3" }, 4).SelectMany(values => values),
                events.OfType<Characters>().Select(text => text.Text));
        }
    }
}
