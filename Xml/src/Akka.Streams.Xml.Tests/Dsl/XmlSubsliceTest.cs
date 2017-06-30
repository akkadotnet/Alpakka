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
    public class XmlSubsliceTest : Akka.TestKit.Xunit.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly Sink<string, Task<IImmutableList<IParseEvent>>> _parser;

        public XmlSubsliceTest(ITestOutputHelper output) : base(output: output)
        {
            _materializer = Sys.Materializer();

            _parser = Flow.Create<string>()
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .Via(XmlParsing.Subslice(new []{"doc", "elem", "item"}.ToImmutableArray()))
                .ToMaterialized(Sink.Seq<IParseEvent>(), Keep.Right);
        }

        [Fact]
        public void XmlSubslice_support_must_properly_extract_subslices_of_events()
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
            fut.Wait(TimeSpan.FromSeconds(3));

            var result = fut.Result;
            ((Characters) result[0]).ShouldBeEquivalentTo(new Characters("i1"));
            ((Characters) result[1]).ShouldBeEquivalentTo(new Characters("i2"));
            ((Characters) result[2]).ShouldBeEquivalentTo(new Characters("i3"));
        }

        [Fact]
        public void XmlSubslice_support_must_properly_extract_subslices_of_nested_events()
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
            fut.Wait(TimeSpan.FromSeconds(3));

            var result = fut.Result;
            ((Characters)result[0]).ShouldBeEquivalentTo(new Characters("i1"));
            ((StartElement) result[1]).ShouldBeEquivalentTo(new StartElement("sub", new Dictionary<string, string>()));
            ((Characters)result[2]).ShouldBeEquivalentTo(new Characters("i2"));
            ((EndElement)result[3]).ShouldBeEquivalentTo(new EndElement("sub"));
            ((Characters)result[4]).ShouldBeEquivalentTo(new Characters("i3"));
        }

        [Fact]
        public void XmlSubslice_support_must_properly_ignore_matches_not_deep_enough()
        {
            var doc =
@"<doc>
  <elem>
    I am lonely here :(
  </elem>
</doc>";

            var fut = Source.Single(doc).RunWith(_parser, _materializer);
            fut.Wait(TimeSpan.FromSeconds(3));

            fut.Result.Count.Should().Be(0);
        }


        [Fact]
        public void XmlSubslice_support_must_properly_ignore_partial_matches()
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
            fut.Wait(TimeSpan.FromSeconds(3));

            fut.Result.Count.Should().Be(0);
        }

        [Fact]
        public void XmlSubslice_support_must_properly_filter_from_the_combination_of_the_above()
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
            fut.Wait(TimeSpan.FromSeconds(3));

            var result = fut.Result;
            ((Characters)result[0]).ShouldBeEquivalentTo(new Characters("i1"));
            ((StartElement)result[1]).ShouldBeEquivalentTo(new StartElement("sub", new Dictionary<string, string>()));
            ((Characters)result[2]).ShouldBeEquivalentTo(new Characters("i2"));
            ((EndElement)result[3]).ShouldBeEquivalentTo(new EndElement("sub"));
            ((Characters)result[4]).ShouldBeEquivalentTo(new Characters("i3"));
            ((Characters)result[5]).ShouldBeEquivalentTo(new Characters("i4"));

        }

        protected override void AfterAll()
        {
            base.AfterAll();
            Sys.Terminate();
        }
    }
}
