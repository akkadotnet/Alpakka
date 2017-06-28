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
            fut.Result.ShouldAllBeEquivalentTo(new List<IParseEvent>()
            {
                new StartDocument(),
                new StartElement("doc", new Dictionary<string, string>()),
                new StartElement("elem", new Dictionary<string, string>()),
                new Characters("elem1"),
                new EndElement("elem"),
                new StartElement("elem", new Dictionary<string, string>()),
                new Characters("elem2"),
                new EndElement("elem"),
                new EndElement("doc"),
                new EndDocument()
            });
        }
    }
}
