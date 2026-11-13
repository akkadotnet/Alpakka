using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;
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
        public async Task CsvFormatting_should_format_simple_value()
        {
            var fut = Source
                .Single(new[] {"eins", "zwei", "drei"}.ToImmutableList())
                .Via(CsvFormatting.Format())
                .RunWith(Sink.First<ByteString>(), Materializer);

            Assert.Equal(ByteString.FromString("eins,zwei,drei\r\n"), (await CompletedWithin(fut)));
        }

        [Fact]
        public async Task CsvFormatting_should_include_Byte_Order_Mark()
        {
            var fut = Source
                .From(new List<ImmutableList<string>>
                {
                    new[] {"eins", "zwei", "drei"}.ToImmutableList(),
                    new[] {"uno", "dos", "tres"}.ToImmutableList()
                })
                .Via(CsvFormatting.Format(byteOrderMark: ByteOrderMark.UTF8))
                .RunWith(Sink.Seq<ByteString>(), Materializer);

            var res = (await CompletedWithin(fut));
            Assert.NotNull(res);
            Assert.Equal(3, res.Count);
            Assert.Equal(ByteOrderMark.UTF8, res[0]);
            Assert.Equal(ByteString.FromString("eins,zwei,drei\r\n"), res[1]);
            Assert.Equal(ByteString.FromString("uno,dos,tres\r\n"), res[2]);
        }
    }
}
