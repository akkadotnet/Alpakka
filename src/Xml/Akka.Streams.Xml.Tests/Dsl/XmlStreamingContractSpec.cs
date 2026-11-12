using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.Xml.Dsl;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Xml.Tests.Dsl;

public sealed class XmlStreamingContractSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    [Theory]
    [InlineData("<root/>", new[] { "StartDocument", "Start:root", "End:root", "EndDocument" })]
    [InlineData("<root></root>", new[] { "StartDocument", "Start:root", "End:root", "EndDocument" })]
    [InlineData("<root/><!--tail-->", new[] { "StartDocument", "Start:root", "End:root", "EndDocument", "Comment:tail" })]
    [InlineData("<root><child/></root>", new[] { "StartDocument", "Start:root", "Start:child", "End:child", "End:root", "EndDocument" })]
    [InlineData("<first/><second/>", new[] { "StartDocument", "Start:first", "End:first", "EndDocument", "StartDocument", "Start:second", "End:second", "EndDocument" })]
    public void Element_events_are_balanced_and_complete_with_exact_demand(string xml, string[] expected)
    {
        var probe = Source.Single(ByteString.FromString(xml)).Via(XmlParsing.Parser())
            .Select(Describe).RunWith(this.SinkProbe<string>(), Sys.Materializer());
        probe.EnsureSubscription();
        foreach (var value in expected)
        {
            probe.Request(1);
            probe.ExpectNext(value);
        }
        probe.ExpectComplete();
    }

    [Theory]
    [InlineData("<root><child></root>")]
    [InlineData("<!DOCTYPE root [<!ENTITY example 'expanded'>]><root>&example;</root>")]
    public async Task Malformed_xml_and_dtds_fail_the_stream(string xml)
    {
        var completion = Source.Single(ByteString.FromString(xml)).Via(XmlParsing.Parser())
            .RunWith(Sink.Seq<IParseEvent>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        await Assert.ThrowsAsync<XmlException>(async () => await completion);
    }

    [Fact]
    public async Task A_text_token_split_across_byte_chunks_is_a_documented_parser_limitation()
    {
        var chunks = new[] { "<doc>", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "</doc>" };
        var completion = Source.From(chunks.Select(ByteString.FromString)).Via(XmlParsing.Parser())
            .RunWith(Sink.Seq<IParseEvent>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        await Assert.ThrowsAsync<XmlException>(async () => await completion);
    }

    [Fact]

    public async Task Namespace_events_retain_local_names_and_utf8_text()
    {
        var xml = "<n:root xmlns:n='urn:test'><n:value n:key='native'>café</n:value></n:root>";
        var completion = Source.Single(ByteString.FromBytes(Encoding.UTF8.GetBytes(xml))).Via(XmlParsing.Parser())
            .RunWith(Sink.Seq<IParseEvent>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        var events = await completion;
        Assert.Equal(new[] { "root", "value" }, events.OfType<StartElement>().Select(element => element.LocalName));
        Assert.Equal("native", events.OfType<StartElement>().Single(element => element.LocalName == "value").Attributes["key"]);
        Assert.Equal("café", Assert.Single(events.OfType<Characters>()).Text);
    }

    [Fact]
    public void Parser_cancellation_reaches_the_byte_source()
    {
        var (source, sink) = this.SourceProbe<ByteString>().Via(XmlParsing.Parser())
            .ToMaterialized(this.SinkProbe<IParseEvent>(), Keep.Both).Run(Sys.Materializer());
        sink.EnsureSubscription().Cancel();
        source.ExpectCancellation();
    }

    private static string Describe(IParseEvent value) => value switch
    {
        StartDocument => "StartDocument",
        EndDocument => "EndDocument",
        StartElement element => "Start:" + element.LocalName,
        EndElement element => "End:" + element.LocalName,
        Comment comment => "Comment:" + comment.Text,
        _ => throw new InvalidOperationException("Unexpected XML event " + value.GetType().Name)
    };
}
