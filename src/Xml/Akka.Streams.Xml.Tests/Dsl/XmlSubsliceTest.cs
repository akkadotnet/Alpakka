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
using static Akka.Streams.Xml.Tests.Dsl.XmlProcessingTest;

namespace Akka.Streams.Xml.Tests.Dsl
{
    public class XmlSubsliceTest : XmlSpec
    {
        private readonly ActorMaterializer _materializer;
        private readonly Sink<string, Task<IImmutableList<IParseEvent>>> _parser;

        public XmlSubsliceTest(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();

            _parser = Flow.Create<string>()
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .Via(XmlParsing.Subslice(new []{"doc", "elem", "item"}.ToImmutableArray()))
                .ToMaterialized(Sink.Seq<IParseEvent>(), Keep.Right);
        }

        [Fact]
        public async Task XmlSubslice_support_must_properly_extract_subslices_of_events()
        {
            var doc =
@"<doc>
  <elem>
    <item>i1</item>
    <item>i2</item>
    <item>i3</item>
  </elem>
</doc>";

            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));
            AssertEvent(new Characters("i1"), result[0]);
            AssertEvent(new Characters("i2"), result[1]);
            AssertEvent(new Characters("i3"), result[2]);
        }

        [Fact]
        public async Task XmlSubslice_support_must_properly_extract_subslices_of_nested_events()
        {
            var doc =
@"<doc>
  <elem>
    <item>i1</item>
    <item><sub>i2</sub></item>
    <item>i3</item>
  </elem>
</doc>";

            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));
            AssertEvent(new Characters("i1"), result[0]);
            AssertEvent(new StartElement("sub", new Dictionary<string, string>()), result[1]);
            AssertEvent(new Characters("i2"), result[2]);
            AssertEvent(new EndElement("sub"), result[3]);
            AssertEvent(new Characters("i3"), result[4]);
        }

        [Fact]
        public async Task XmlSubslice_support_must_properly_ignore_matches_not_deep_enough()
        {
            var doc =
@"<doc>
  <elem>
    I am lonely here :(
  </elem>
</doc>";

            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            Assert.Empty(await CompletedWithin(fut));
        }


        [Fact]
        public async Task XmlSubslice_support_must_properly_ignore_partial_matches()
        {
            var doc =
@"<doc>
  <elem>
    <notanitem>ignore me</notanitem>
    <notanitem>ignore me</notanitem>
    <foo>ignore me</foo>
  </elem>
</doc>";

            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            Assert.Empty(await CompletedWithin(fut));
        }

        [Fact]
        public async Task XmlSubslice_support_must_properly_filter_from_the_combination_of_the_above()
        {
            var doc =
@"<doc>
  <elem>
    <notanitem>ignore me</notanitem>
    <notanitem>ignore me</notanitem>
    <foo>ignore me</foo>
    <item>i1</item>
    <item><sub>i2</sub></item>
    <item>i3</item>
  </elem>
  <elem>
    not me please
  </elem>
  <elem><item>i4</item></elem>
</doc>";

            var fut = Source.Single(doc).RunWith(_parser, _materializer);

            var result = (await CompletedWithin(fut));
            AssertEvent(new Characters("i1"), result[0]);
            AssertEvent(new StartElement("sub", new Dictionary<string, string>()), result[1]);
            AssertEvent(new Characters("i2"), result[2]);
            AssertEvent(new EndElement("sub"), result[3]);
            AssertEvent(new Characters("i3"), result[4]);
            AssertEvent(new Characters("i4"), result[5]);

        }
    }
}
