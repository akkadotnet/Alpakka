using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Csv.Tests.dsl;

public sealed class CsvStreamingContractSpec(ITestOutputHelper output) : Akka.TestKit.Xunit2.TestKit(output: output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task Utf8_bom_quotes_and_crlf_are_preserved_across_byte_chunk_boundaries(int chunkSize)
    {
        var input = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("name,note\r\ncafé,\"a,\"\"quoted\"\"\r\nb\"\r\nlast,end")).ToArray();
        var chunks = Enumerable.Range(0, (input.Length + chunkSize - 1) / chunkSize)
            .Select(i => ByteString.FromBytes(input.Skip(i * chunkSize).Take(chunkSize).ToArray()));
        var completion = Source.From(chunks).Via(CsvParsing.LineScanner())
            .Select(row => row.Select(value => value.ToString(Encoding.UTF8)).ToArray())
            .RunWith(Sink.Seq<string[]>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        var actual = await completion;
        Assert.Equal(3, actual.Count);
        Assert.Equal(new[] { "name", "note" }, actual[0]);
        Assert.Equal(new[] { "café", "a,\"quoted\"\r\nb" }, actual[1]);
        Assert.Equal(new[] { "last", "end" }, actual[2]);
    }

    [Theory]
    [InlineData("a\\", ",b,c\n", new[] { "a,b", "c" })]
    [InlineData("\"a\\", "\"b\",c\n", new[] { "a\"b", "c" })]
    [InlineData("\"a\\", "\\b\",c\n", new[] { "a\\b", "c" })]
    [InlineData("a,\\", ",b\n", new[] { "a", ",b" })]
    [InlineData("a,\"\\", "\"b\"\n", new[] { "a", "\"b" })]
    public async Task An_escape_at_a_chunk_boundary_waits_for_its_following_byte(string first, string second, string[] expected)
    {
        var completion = Source.From(new[] { ByteString.FromString(first), ByteString.FromString(second) })
            .Via(CsvParsing.LineScanner()).RunWith(Sink.Seq<ImmutableList<ByteString>>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        var row = Assert.Single(await completion);
        Assert.Equal(expected, row.Select(value => value.ToString()).ToArray());
    }

    [Theory]
    [InlineData("a,\\")]
    [InlineData("a\\")]
    [InlineData("a,\"\\")]
    [InlineData("\"a\\")]
    public async Task A_dangling_escape_at_stream_eof_still_fails(string input)
    {
        var completion = Source.Single(ByteString.FromString(input)).Via(CsvParsing.LineScanner())
            .RunWith(Sink.Seq<ImmutableList<ByteString>>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        await Assert.ThrowsAsync<MalformedCsvException>(async () => await completion);
    }

    [Theory]
    [InlineData("a\\", "x\n")]
    [InlineData("\"a\\", "x\"\n")]
    public async Task An_invalid_escape_after_a_chunk_boundary_still_fails(string first, string second)
    {
        var completion = Source.From(new[] { ByteString.FromString(first), ByteString.FromString(second) })
            .Via(CsvParsing.LineScanner()).RunWith(Sink.Seq<ImmutableList<ByteString>>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        await Assert.ThrowsAsync<MalformedCsvException>(async () => await completion);
    }

    [Fact]
    public async Task Standalone_cr_and_extra_blank_lines_survive_crlf_boundary_handling()
    {
        var completion = Source.From(new[] { "first,1\r", "", "second,2\r", "\n\nlast,3" }.Select(ByteString.FromString))
            .Via(CsvParsing.LineScanner()).RunWith(Sink.Seq<ImmutableList<ByteString>>(), Sys.Materializer());
        await AwaitAssertAsync(() => Assert.True(completion.IsCompleted));
        var rows = await completion;
        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "first", "1" }, rows[0].Select(value => value.ToString()));
        Assert.Equal(new[] { "second", "2" }, rows[1].Select(value => value.ToString()));
        Assert.Equal(new[] { "" }, rows[2].Select(value => value.ToString()));
        Assert.Equal(new[] { "last", "3" }, rows[3].Select(value => value.ToString()));
    }

    [Fact]
    public void Buffered_final_rows_respect_demand_and_completion_without_an_extra_pull()
    {
        var (source, sink) = this.SourceProbe<ByteString>().Via(CsvParsing.LineScanner())
            .Select(row => row.Select(value => value.ToString()).ToArray())
            .ToMaterialized(this.SinkProbe<string[]>(), Keep.Both).Run(Sys.Materializer());
        source.SendNext(ByteString.FromString("first,1\r\nsecond,2\r\nlast,3"));
        source.SendComplete();
        sink.EnsureSubscription().ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        Assert.Equal(new[] { "first", "1" }, sink.Request(1).ExpectNext());
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        Assert.Equal(new[] { "second", "2" }, sink.Request(1).ExpectNext());
        Assert.Equal(new[] { "last", "3" }, sink.Request(1).ExpectNext());
        sink.ExpectComplete();
    }

    [Fact]
    public void Downstream_cancellation_reaches_the_byte_source()
    {
        var (source, sink) = this.SourceProbe<ByteString>().Via(CsvParsing.LineScanner())
            .ToMaterialized(this.SinkProbe<ImmutableList<ByteString>>(), Keep.Both).Run(Sys.Materializer());
        sink.EnsureSubscription().Cancel();
        source.ExpectCancellation();
    }
}
