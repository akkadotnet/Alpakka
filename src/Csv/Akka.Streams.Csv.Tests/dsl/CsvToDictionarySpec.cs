using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Csv.Tests.dsl
{
    public class CsvToDictionarySpec : CsvSpec
    {
        public CsvToDictionarySpec(ITestOutputHelper output) : base(output)
        {}

        [Fact]
        public void CsvToDictionary_should_parse_header_line_and_data_line_into_dictionary()
        {
            var fut = Source.Single(ByteString.FromString("eins,zwei,drei\n1,2,3"))
                .Via(CsvParsing.LineScanner())
                .Via(CsvToDictionary.ToDictionary())
                .RunWith(Sink.First<Dictionary<string, ByteString>>(), Materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.Should().BeEquivalentTo(new Dictionary<string, ByteString>
            {
                {"eins", ByteString.FromString("1") },
                {"zwei", ByteString.FromString("2") },
                {"drei", ByteString.FromString("3") },
            }, opt => opt.WithStrictOrdering());
        }

        [Fact]
        public void CsvToDictionary_should_be_OK_with_fewer_header_columns_than_data()
        {
            var fut = Source.Single(ByteString.FromString("eins,zwei\n1,2,3"))
                .Via(CsvParsing.LineScanner())
                .Via(CsvToDictionary.ToDictionary())
                .RunWith(Sink.First<Dictionary<string, ByteString>>(), Materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.Should().BeEquivalentTo(new Dictionary<string, ByteString>
            {
                {"eins", ByteString.FromString("1") },
                {"zwei", ByteString.FromString("2") },
            }, opt => opt.WithStrictOrdering());
        }

        [Fact]
        public void CsvToDictionary_should_be_OK_with_more_header_columns_than_data()
        {
            var fut = Source.Single(ByteString.FromString("eins,zwei,drei,vier\n1,2,3"))
                .Via(CsvParsing.LineScanner())
                .Via(CsvToDictionary.ToDictionary())
                .RunWith(Sink.First<Dictionary<string, ByteString>>(), Materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.Should().BeEquivalentTo(new Dictionary<string, ByteString>
            {
                {"eins", ByteString.FromString("1") },
                {"zwei", ByteString.FromString("2") },
                {"drei", ByteString.FromString("3") },
            }, opt => opt.WithStrictOrdering());
        }

        [Fact]
        public void CsvToDictionary_should_use_column_names_and_data_line_into_map()
        {
            var fut = Source.Single(ByteString.FromString("1,2,3"))
                .Via(CsvParsing.LineScanner())
                .Via(CsvToDictionary.WithHeaders(new []{ "eins", "zwei", "drei" }.ToImmutableList()))
                .RunWith(Sink.First<Dictionary<string, ByteString>>(), Materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.Should().BeEquivalentTo(new Dictionary<string, ByteString>
            {
                {"eins", ByteString.FromString("1") },
                {"zwei", ByteString.FromString("2") },
                {"drei", ByteString.FromString("3") },
            }, opt => opt.WithStrictOrdering());

        }
    }
}
