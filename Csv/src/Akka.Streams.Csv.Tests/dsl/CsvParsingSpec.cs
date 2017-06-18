using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Csv.Tests.dsl
{
    public class CsvParsingSpec : Akka.TestKit.Xunit.TestKit
    {
        private readonly ActorMaterializer _materializer;

        public CsvParsingSpec(ITestOutputHelper output) : base(output: output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void CsvParsing_should_parse_one_line()
        {
            var fut = Source
                .Single(ByteString.FromString("eins,zwei,drei\n"))
                .Via(CsvParsing.LineScanner())
                .RunWith(Sink.First<ImmutableList<ByteString>>(), _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            fut.Result.ShouldAllBeEquivalentTo(new[] { ByteString.FromString("eins"), ByteString.FromString("zwei"), ByteString.FromString("drei") });
        }

        [Fact]
        public void CsvParsing_should_parse_two_line()
        {
            var fut = Source
                .Single(ByteString.FromString("eins,zwei,drei\nuno,dos,tres\n"))
                .Via(CsvParsing.LineScanner())
                .RunWith(Sink.Seq<ImmutableList<ByteString>>(), _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new[] { ByteString.FromString("eins"), ByteString.FromString("zwei"), ByteString.FromString("drei") });
            res[1].ShouldAllBeEquivalentTo(new[] { ByteString.FromString("uno"), ByteString.FromString("dos"), ByteString.FromString("tres") });
        }

        [Fact]
        public void CsvParsing_should_parse_two_line_even_without_line_end()
        {
            var fut = Source
                .Single(ByteString.FromString("eins,zwei,drei\nuno,dos,tres"))
                .Via(CsvParsing.LineScanner())
                .RunWith(Sink.Seq<ImmutableList<ByteString>>(), _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new[] { ByteString.FromString("eins"), ByteString.FromString("zwei"), ByteString.FromString("drei") });
            res[1].ShouldAllBeEquivalentTo(new[] { ByteString.FromString("uno"), ByteString.FromString("dos"), ByteString.FromString("tres") });
        }

        [Fact]
        public void CsvParsing_should_parse_semicolon_lines()
        {
            var fut = Source
                .Single(ByteString.FromString("eins;zwei;drei\nein”s;zw ei;dr\\ei\nun’o;dos;tres\n"))
                .Via(CsvParsing.LineScanner(delimiter: CsvParsing.SemiColon, escapeChar: 0x2a/*'*'*/))
                .Select(list =>
                {
                    var outList = new List<string>();
                    foreach (var bs in list)
                    {
                        outList.Add(bs.DecodeString());
                    }
                    return outList.ToArray();
                })
                .RunWith(Sink.Seq<string[]>(), _materializer);

            fut.Wait(TimeSpan.FromSeconds(3));
            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new[] { "eins", "zwei", "drei" });
            res[1].ShouldAllBeEquivalentTo(new[] { "ein”s", "zw ei", "dr\\ei" });
        }

        [Fact]
        public void CsvParsing_should_parse_chunks_successfully()
        {
            var input = new[]
            {
                ByteString.FromString("eins,zw"),
                ByteString.FromString("ei,drei\nuno"),
                ByteString.FromString(",dos,tres\n")
            };
            var fut = Source
                .From(input)
                .Via(CsvParsing.LineScanner())
                .Select(list =>
                {
                    var outList = new List<string>();
                    foreach (var bs in list)
                    {
                        outList.Add(bs.DecodeString());
                    }
                    return outList.ToArray();
                })
                .RunWith(Sink.Seq<string[]>(), _materializer);
            fut.Wait(TimeSpan.FromSeconds(3));
            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new[] { "eins", "zwei", "drei" });
            res[1].ShouldAllBeEquivalentTo(new[] { "uno", "dos", "tres" });
        }

        [Fact]
        public void CsvParsing_should_emit_completion_even_without_new_line_at_end()
        {
            var t = this.SourceProbe<ByteString>()
                .Via(CsvParsing.LineScanner())
                .Select(list =>
                {
                    var outList = new List<string>();
                    foreach (var bs in list)
                    {
                        outList.Add(bs.DecodeString(Encoding.UTF8));
                    }
                    return outList.ToArray();
                })
                .ToMaterialized(this.SinkProbe<string[]>(), Keep.Both)
                .Run(_materializer);
            var source = t.Item1;
            var sink = t.Item2;

            source.SendNext(ByteString.FromString("eins,zwei,drei\nuno,dos,tres\n1,2,3"));
            sink.Request(3);
            sink.ExpectNext().ShouldAllBeEquivalentTo(new[] { "eins", "zwei", "drei" });
            sink.ExpectNext().ShouldAllBeEquivalentTo(new[] { "uno", "dos", "tres" });
            sink.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            source.SendComplete();
            sink.ExpectNext().ShouldAllBeEquivalentTo(new[] { "1", "2", "3" });
            sink.ExpectComplete();
        }

        [Fact]
        public void CsvParsing_should_parse_Apple_Numbers_exported_file()
        {
            var fut = FileIO.FromFile(new FileInfo("resources/numbers-utf-8.csv"))
                .Via(CsvParsing.LineScanner(delimiter: CsvParsing.SemiColon, escapeChar: 0x01))
                .Select(list =>
                {
                    var outList = new List<string>();
                    foreach (var bs in list)
                    {
                        outList.Add(bs.DecodeString(Encoding.UTF8));
                    }
                    return outList.ToArray();
                })
                .RunWith(Sink.Seq<string[]>(), _materializer);

            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new[] { "abc", "def", "ghi", "", "", "", "" });
            res[1].ShouldAllBeEquivalentTo(new[] { "\"", "\\\\;", "a\"\nb\"\"c", "", "", "", "" });
        }

        [Fact]
        public void CsvParsing_should_parse_Google_Docs_exported_file()
        {
            var fut = FileIO.FromFile(new FileInfo("resources/google-docs.csv"))
            .Via(CsvParsing.LineScanner(escapeChar: 0x01))
            .Select(list =>
            {
                var outList = new List<string>();
                foreach (var bs in list)
                {
                    outList.Add(bs.DecodeString(Encoding.UTF8));
                }
                return outList.ToArray();
            })
            .RunWith(Sink.Seq<string[]>(), _materializer);

            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new[] { "abc", "def", "ghi" });
            res[1].ShouldAllBeEquivalentTo(new[] { "\"", "\\\\,", "a\"\nb\"\"c" });
        }

        [Fact]
        // see https://github.com/uniVocity/csv-parsers-comparison
        public void CsvParsing_should_parse_uniVocity_correctness_test()
        {
            var fut = FileIO.FromFile(new FileInfo("resources/correctness.csv"))
                .Via(CsvParsing.LineScanner(escapeChar: 0x01))
                .Via(CsvToDictionary.ToDictionary())
                .Select(dict =>
                {
                    var outDict = new Dictionary<string, string>();
                    foreach (var pair in dict)
                    {
                        outDict.Add(pair.Key, pair.Value.DecodeString(Encoding.UTF8));
                    }
                    return outDict;
                })
                .RunWith(Sink.Seq<Dictionary<string, string>>(), _materializer);

            var res = fut.Result;
            res[0].ShouldAllBeEquivalentTo(new Dictionary<string, string>()
            {
                { "Year", "1997" },
                { "Make" , "Ford" },
                { "Model" , "E350" },
                { "Description" , "ac, abs, moon" },
                { "Price" , "3000.00" },
            });
            res[1].ShouldAllBeEquivalentTo(new Dictionary<string, string>()
            {
                { "Year", "1999" },
                { "Make" , "Chevy" },
                { "Model" , "Venture \"Extended Edition\"" },
                { "Description" , "" },
                { "Price" , "4900.00" },
            });
            res[2].ShouldAllBeEquivalentTo(new Dictionary<string, string>()
            {
                { "Year", "1996" },
                { "Make" , "Jeep" },
                { "Model" , "Grand Cherokee" },
                { "Description" , "MUST SELL!\nair, moon roof, loaded" },
                { "Price" , "4799.00" },
            });
            res[3].ShouldAllBeEquivalentTo(new Dictionary<string, string>()
            {
                { "Year", "1999" },
                { "Make" , "Chevy" },
                { "Model" , "Venture \"Extended Edition, Very Large\"" },
                { "Description" , "" },
                { "Price" , "5000.00" },
            });
            res[4].ShouldAllBeEquivalentTo(new Dictionary<string, string>()
            {
                { "Year", "" },
                { "Make" , "" },
                { "Model" , "Venture \"Extended Edition\"" },
                { "Description" , "" },
                { "Price" , "4900.00" },
            });
        }
    }
}
