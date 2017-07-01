using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Xml.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Xml.Tests.Dsl
{
    public class XmlProcessingTest : Akka.TestKit.Xunit.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly Sink<string, Task<IImmutableList<IParseEvent>>> _parser;

        public XmlProcessingTest(ITestOutputHelper output) : base(output: output)
        {
            _materializer = Sys.Materializer();

            _parser = Flow.Create<string>()
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .ToMaterialized(Sink.Seq<IParseEvent>(), Keep.Right);
        }

        [Fact]
        public void XmlParser_must_properly_parse_simple_XML()
        {
            var doc = "<doc><elem>elem1</elem><elem>elem2</elem></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);
            
            fut.Wait(TimeSpan.FromSeconds(3));
            var result = fut.Result;

            result[0].Should().BeOfType<StartDocument>();
            (result[1] as StartElement).ShouldBeEquivalentTo(new StartElement("doc", new Dictionary<string, string>()));
            (result[2] as StartElement).ShouldBeEquivalentTo(new StartElement("elem", new Dictionary<string, string>()));
            (result[3] as Characters).ShouldBeEquivalentTo(new Characters("elem1"));
            (result[4] as EndElement).ShouldBeEquivalentTo(new EndElement("elem"));
            (result[5] as StartElement).ShouldBeEquivalentTo(new StartElement("elem", new Dictionary<string, string>()));
            (result[6] as Characters).ShouldBeEquivalentTo(new Characters("elem2"));
            (result[7] as EndElement).ShouldBeEquivalentTo(new EndElement("elem"));
            (result[8] as EndElement).ShouldBeEquivalentTo(new EndElement("doc"));
            result[9].Should().BeOfType<EndDocument>();
        }

        [Fact]
        public void XmlParser_must_properly_process_a_comment()
        {
            var doc = "<doc><!--comment--></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var result = fut.Result;

            result[0].Should().BeOfType<StartDocument>();
            (result[1] as StartElement).ShouldBeEquivalentTo(new StartElement("doc", new Dictionary<string, string>()));
            (result[2] as Comment).ShouldBeEquivalentTo(new Comment("comment"));
            (result[3] as EndElement).ShouldBeEquivalentTo(new EndElement("doc"));
            result[4].Should().BeOfType<EndDocument>();
        }

        [Fact]
        public void XmlParser_must_properly_process_parse_instructions()
        {
            var doc = "<?target content?><doc></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var result = fut.Result;

            result[0].Should().BeOfType<StartDocument>();
            (result[1] as ProcessingInstruction).ShouldBeEquivalentTo(new ProcessingInstruction("target", "content"));
            (result[2] as StartElement).ShouldBeEquivalentTo(new StartElement("doc", new Dictionary<string, string>()));
            (result[3] as EndElement).ShouldBeEquivalentTo(new EndElement("doc"));
            result[4].Should().BeOfType<EndDocument>();
        }

        [Fact]
        public void XmlParser_must_properly_process_attributes()
        {
            var doc = "<doc good=\"yes\"><elem nice=\"yes\" very=\"true\">elem1</elem></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var result = fut.Result;

            result[0].Should().BeOfType<StartDocument>();
            (result[1] as StartElement).ShouldBeEquivalentTo(new StartElement("doc", new Dictionary<string, string> {{"good", "yes"}}));
            (result[2] as StartElement).ShouldBeEquivalentTo(new StartElement("elem", new Dictionary<string, string> {{"nice", "yes"}, {"very", "true"}}));
            (result[3] as Characters).ShouldBeEquivalentTo(new Characters("elem1"));
            (result[4] as EndElement).ShouldBeEquivalentTo(new EndElement("elem"));
            (result[5] as EndElement).ShouldBeEquivalentTo(new EndElement("doc"));
            result[6].Should().BeOfType<EndDocument>();
        }

        [Fact]
        public void XmlParser_must_properly_process_CDATA_blocks()
        {
            var doc = "<doc><![CDATA[<not>even</valid>]]></doc>";
            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var result = fut.Result;

            result[0].Should().BeOfType<StartDocument>();
            (result[1] as StartElement).ShouldBeEquivalentTo(new StartElement("doc", new Dictionary<string, string>()));
            (result[2] as CData).ShouldBeEquivalentTo(new CData("<not>even</valid>"));
            (result[3] as EndElement).ShouldBeEquivalentTo(new EndElement("doc"));
            result[4].Should().BeOfType<EndDocument>();
        }

        // I'm not sure what the original very convoluted code does
        [Fact]
        public void XmlParser_must_properly_parse_large_XML()
        {
            var elements = Enumerable.Range(0, 10).Select(i => i.ToString()).ToImmutableArray();

            var documentStream = Source
                .Single("<doc>")
                .Concat(Source.FromEnumerator(elements.Select(s => $"<elem>{s}</elem>").GetEnumerator))
                .Concat(Source.Single("</doc>"));

            var fut = documentStream
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .Collect(evt => evt is Characters ? evt : null)
                .Collect(evt => ((Characters) evt).Text)
                .RunWith(Sink.Seq<string>(), _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.ShouldAllBeEquivalentTo(elements);
        }

        [Fact]
        public void XmlParser_must_properly_parse_chunks_bigger_than_its_buffer_size()
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

            fut.Wait(TimeSpan.FromSeconds(3));
            var res = fut.Result;
            //fut.Invoking(f => f.Wait(TimeSpan.FromSeconds(3))).ShouldNotThrow();
        }
    }
}
