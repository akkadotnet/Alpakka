using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Xml.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Xml.Tests.Dsl
{
    public class XmlCoalesceTest:Akka.TestKit.Xunit.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly Sink<string, Task<IImmutableList<IParseEvent>>> _parser;

        public XmlCoalesceTest(ITestOutputHelper output) : base(output:output)
        {
            _materializer = Sys.Materializer();

            _parser = Flow.Create<string>()
                .Select(ByteString.FromString)
                .Via(XmlParsing.Parser())
                .Via(XmlParsing.Coalesce(10))
                .ToMaterialized(Sink.Seq<IParseEvent>(), Keep.Right);
        }

        [Fact]
        public void XmlCoalesce_support_must_properly_unify_a_chain_of_character_chunks()
        {
            var fut = Source.Single("<doc>")
                .Concat(Source.FromEnumerator(Enumerable.Range(0, 10).Select(i => i.ToString()).GetEnumerator))
                .Concat(Source.Single("</doc>"))
                .RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.ShouldAllBeEquivalentTo(new List<IParseEvent>()
            {
                new StartDocument(),
                new StartElement("doc", new Dictionary<string, string>()),
                new Characters("0123456789"),
                new EndElement("doc"),
                new EndDocument()
            });
        }

        [Fact]
        public void XmlCoalesce_support_must_properly_unify_a_chain_of_CDATA_chunks()
        {
            var fut = Source.Single("<doc>")
                .Concat(Source.FromEnumerator(Enumerable.Range(0, 10).Select(i => $"<![CDATA[{i}]]>").GetEnumerator))
                .Concat(Source.Single("</doc>"))
                .RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.ShouldAllBeEquivalentTo(new List<IParseEvent>()
            {
                new StartDocument(),
                new StartElement("doc", new Dictionary<string, string>()),
                new Characters("0123456789"),
                new EndElement("doc"),
                new EndDocument()
            });
        }

        [Fact]
        public void XmlCoalesce_support_must_properly_unify_a_chain_of_CDATA_and_character_chunks()
        {
            var fut = Source.Single("<doc>")
                .Concat(Source.FromEnumerator(Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? $"<![CDATA[{i}]]>" : i.ToString()).GetEnumerator))
                .Concat(Source.Single("</doc>"))
                .RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.ShouldAllBeEquivalentTo(new List<IParseEvent>()
            {
                new StartDocument(),
                new StartElement("doc", new Dictionary<string, string>()),
                new Characters("0123456789"),
                new EndElement("doc"),
                new EndDocument()
            });
        }

        [Fact]
        public void XmlCoalesce_support_must_properly_report_an_error_if_text_limit_is_exceeded()
        {
            var fut = Source.Single("<doc>")
                .Concat(Source.FromEnumerator(Enumerable.Range(0, 10).Select(i => i.ToString()).GetEnumerator))
                .Concat(Source.Single("</doc>"))
                .RunWith(_parser, _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.IsFaulted.Should().Be(true);
            fut.Exception.InnerException.Should().BeOfType<IllegalStateException>();
        }

        protected override void AfterAll()
        {
            base.AfterAll();
            Sys.Terminate();
        }
    }
}
