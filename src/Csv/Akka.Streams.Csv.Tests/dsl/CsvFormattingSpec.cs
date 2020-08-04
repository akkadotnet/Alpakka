using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Csv.Tests.dsl
{
    public class CsvFormattingSpec: CsvSpec
    {
        public CsvFormattingSpec(ITestOutputHelper output) : base(output: output)
        {
        }

        [Fact]
        public void CsvFormatting_should_format_simple_value()
        {
            var fut = Source
                .Single(new[] {"eins", "zwei", "drei"}.ToImmutableList())
                .Via(CsvFormatting.Format())
                .RunWith(Sink.First<ByteString>(), Materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.Should().BeEquivalentTo(ByteString.FromString("eins,zwei,drei\r\n"));
        }

        [Fact]
        public void CsvFormatting_should_include_Byte_Order_Mark()
        {
            var fut = Source
                .From(new List<ImmutableList<string>>
                {
                    new[] {"eins", "zwei", "drei"}.ToImmutableList(),
                    new[] {"uno", "dos", "tres"}.ToImmutableList()
                })
                .Via(CsvFormatting.Format(byteOrderMark: ByteOrderMark.UTF8))
                .RunWith(Sink.Seq<ByteString>(), Materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var res = fut.Result;
            res.Should().NotBeNull();
            res.Count.Should().Be(3);
            res[0].Should().BeEquivalentTo(ByteOrderMark.UTF8);
            res[1].Should().BeEquivalentTo(ByteString.FromString("eins,zwei,drei\r\n"));
            res[2].Should().BeEquivalentTo(ByteString.FromString("uno,dos,tres\r\n"));
        }
    }
}
