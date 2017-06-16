using System.Collections.Generic;
using System.Text;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Csv.Tests
{
    public class CsvParserSpec : Akka.TestKit.Xunit.TestKit
    {
        public CsvParserSpec(ITestOutputHelper output) : base(output: output)
        {
        }

        [Fact]
        public void CsvParser_should_read_comma_separated_values_into_a_list()
        {
            ExpectInOut("one,two,three\n", new[,] {{"one", "two", "three"}});
        }

        [Fact]
        public void CsvParser_should_not_deliver_input_until_end_of_line_is_reached()
        {
            ExpectInOut("one,two,three");
        }

        [Fact]
        public void CsvParser_should_read_a_line_if_line_end_is_not_required()
        {
            ExpectInOut("one,two,three", new[,] {{"one", "two", "three"}}, requireLineEnd: false);
        }

        [Fact]
        public void CsvParser_should_read_two_lines_into_two_lists()
        {
            ExpectInOut("one,two,three\n1,2,3\n", new[,] {{"one", "two", "three"}, {"1", "2", "3"}});
        }

        [Fact]
        public void CsvParser_should_parse_empty_input_to_null()
        {
            var parser = new CsvParser(CsvParsing.Comma, 0x2d, 0x2e);
            parser.Offer(ByteString.Empty);
            parser.Poll(true).ShouldBeEquivalentTo(null);
        }

        [Fact]
        public void CsvParser_should_parse_leading_comma_to_be_an_empty_column()
        {
            ExpectInOut(",one,two,three\n", new[,] {{"", "one", "two", "three"}});
        }

        [Fact]
        public void CsvParser_should_parse_trailing_comma_to_be_an_empty_column()
        {
            ExpectInOut("one,two,three,\n", new[,] { { "one", "two", "three", "" } });
        }

        [Fact]
        public void CsvParser_should_parse_double_comma_to_be_an_empty_column()
        {
            ExpectInOut("one,,two,three\n", new[,] { { "one", "", "two", "three"} });
        }

        [Fact]
        public void CsvParser_should_parse_an_empty_line_with_LF_into_a_single_column()
        {
            ExpectInOut("\n", new[,] {{""}});
        }

        [Fact]
        public void CsvParser_should_parse_an_empty_line_with_CR_LF_into_a_single_column()
        {
            ExpectInOut("\r\n", new[,] { { "" } });
        }

        [Fact]
        public void CsvParser_should_parse_UTF8_chars_unchanged()
        {
            ExpectInOut("ℵ,a,ñÅë,อักษรไทย\n", new[,] {{"ℵ", "a", "ñÅë", "อักษรไทย"}});
        }

        [Fact]
        public void CsvParser_should_parse_double_quote_chars_into_empty_column()
        {
            ExpectInOut("a,\"\",c\n", new [,]{{"a", "", "c"}});
        }

        [Fact]
        public void CsvParser_should_parse_double_quote_within_quotes_into_one_quote()
        {
            ExpectInOut("a,\"\"\"\",c\n", new[,] {{"a", "\"", "c"}});
        }

        [Fact]
        public void CsvParser_should_parse_double_quote_within_quotes_into_one_quote_at_end_of_value()
        {
            ExpectInOut("\"Venture \"\"Extended Edition\"\"\",\"\",4900.00\n",
                new[,] {{"Venture \"Extended Edition\"", "", "4900.00"}});
        }

        [Fact]
        public void CsvParser_should_parse_double_escape_chars_into_one_escape_char()
        {
            ExpectInOut("a,\\\\,c\n", new[,] {{"a", "\\", "c"}});
        }

        [Fact]
        public void CsvParser_should_parse_quoted_escape_chars_into_one_escape_char()
        {
            ExpectInOut("a,\"\\\\\",c\n", new[,]{{ "a", "\\", "c" } });
        }

        [Fact]
        public void CsvParser_should_parse_escaped_comma_into_comma()
        {
            ExpectInOut("a,\\,,c\n", new[,]{{ "a", ",", "c" } });
        }

        [Fact]
        public void CsvParser_should_fail_on_escaped_quote_as_quotes_are_escaped_by_doubled_quote_chars()
        {
            var parser = new CsvParser(CsvParsing.Comma, CsvParsing.DoubleQuote, CsvParsing.Backslash);
            parser.Offer(ByteString.FromString("a,\\\",c\n"));
            parser.Invoking(p => p.Poll(true))
                .ShouldThrowExactly<MalformedCsvException>()
                .WithMessage("wrong escaping at 1:2, only escape or delimiter may be escaped");
        }

        [Fact]
        public void CsvParser_should_fail_on_escape_at_line_end()
        {
            var parser = new CsvParser(CsvParsing.Comma, CsvParsing.DoubleQuote, CsvParsing.Backslash);
            parser.Offer(ByteString.FromString("a,\\"));
            parser.Invoking(p => p.Poll(true))
                .ShouldThrowExactly<MalformedCsvException>()
                .WithMessage("wrong escaping at 1:2, no character after escape");
        }

        [Fact]
        public void CsvParser_should_fail_on_escape_within_field_at_line_end()
        {
            var parser = new CsvParser(CsvParsing.Comma, CsvParsing.DoubleQuote, CsvParsing.Backslash);
            parser.Offer(ByteString.FromString("a,b\\"));
            parser.Invoking(p => p.Poll(true))
                .ShouldThrowExactly<MalformedCsvException>()
                .WithMessage("wrong escaping at 1:3, no character after escape");
        }

        [Fact]
        public void CsvParser_should_fail_on_escape_within_quoted_field_at_line_end()
        {
            var parser = new CsvParser(CsvParsing.Comma, CsvParsing.DoubleQuote, CsvParsing.Backslash);
            parser.Offer(ByteString.FromString("a,\"\\"));
            parser.Invoking(p => p.Poll(true))
                .ShouldThrowExactly<MalformedCsvException>()
                .WithMessage("wrong escaping at 1:3, no character after escape");
        }

        [Fact]
        public void CsvParser_should_parse_escaped_escape_within_quotes_into_quote()
        {
            ExpectInOut("a,\"\\\\\",c\n", new[,] {{"a", "\\", "c"}});
        }

        [Fact]
        public void CsvParser_should_parse_escaped_quote_within_quotes_into_quote()
        {
            ExpectInOut("a,\"\\\"\",c\n", new[,] {{"a", "\"", "c"}});
        }

        [Fact]
        public void CsvParser_should_parse_escaped_escape_in_text_within_quotes_into_quote()
        {
            ExpectInOut("a,\"abc\\\\def\",c\n", new[,]{{ "a", "abc\\def", "c" } });
        }

        [Fact]
        public void CsvParser_should_parse_escaped_quote_in_text_within_quotes_into_quote()
        {
            ExpectInOut("a,\"abc\\\"def\",c\n", new[,] {{"a", "abc\"def", "c"}});
        }

        [Fact(Skip = "Unicode separator was not implemented in original scala class")]
        public void CsvParser_should_allow_Unicode_L_SEP_0x2028_as_line_separator()
        {
            var parser = new CsvParser(CsvParsing.Comma, CsvParsing.DoubleQuote, CsvParsing.Backslash);
            parser.Offer(ByteString.FromString("abc\u2028"));
            var res = parser.Poll(true);
            res[0].DecodeString().ShouldBeEquivalentTo("abc");
        }

        [Fact]
        public void CsvParser_should_ignore_trailing_LF()
        {
            ExpectInOut("one,two,three\n", new[,] {{"one", "two", "three"}});
        }

        [Fact]
        public void CsvParser_should_ignore_trailing_CR_LF()
        {
            ExpectInOut("one,two,three\r\n", new[,] {{"one", "two", "three"}});
        }

        [Fact]
        public void CsvParser_should_keep_whitespace()
        {
            ExpectInOut("one, two ,three  \n", new[,] {{"one", " two ", "three  "}});
        }

        [Fact]
        public void CsvParser_should_quoted_values_keep_whitespace()
        {
            ExpectInOut("\" one \",\" two \",\"three  \"\n", new[,] {{" one ", " two ", "three  "}});
        }

        [Fact]
        public void CsvParser_should_quoted_values_may_contain_LF()
        {
            ExpectInOut("one,\"two\ntwo\",three\n1,2,3\n", new[,] {{"one", "two\ntwo", "three"}, {"1", "2", "3"}});
        }

        [Fact]
        public void CsvParser_should_quoted_values_may_contain_CR_LF()
        {
            ExpectInOut("one,\"two\r\ntwo\",three\n", new[,] {{"one", "two\r\ntwo", "three"}});
        }

        [Fact]
        public void CsvParser_should_take_double_double_quote_as_single_double_quote()
        {
            ExpectInOut("one,\"tw\"\"o\",three\n", new[,] {{"one", "tw\"o", "three"}});
        }

        [Fact]
        public void CsvParser_should_read_values_with_different_separator()
        {
            ExpectInOut("$Foo $#$Bar $#$Baz $\n", new[,] {{"Foo ", "Bar ", "Baz "}},
                0x23, // '#'
                0x24  // '$'
            );
        }

        [Fact]
        public void Csv_parsing_with_Byte_Order_Mark_should_accept_UTF8_BOM()
        {
            var bsIn = ByteOrderMark.UTF8 + ByteString.FromString("one,two,three\n", Encoding.UTF8);
            ExpectBsInOut(bsIn, new[,] {{"one", "two", "three"}});
        }

        [Fact]
        public void Csv_parsing_with_Byte_Order_Mark_should_fail_for_UTF16_LE_BOM()
        {
            var bsIn = ByteOrderMark.UTF16_LE + ByteString.FromString("one,two,three\n", Encoding.Unicode);
            this.Invoking(t => t.ExpectBsInOut(bsIn, new[,] {{"one", "two", "three"}}))
                .ShouldThrowExactly<UnsupportedCharsetException>();
        }

        [Fact]
        public void Csv_parsing_with_Byte_Order_Mark_should_fail_for_UTF16_BE_BOM()
        {
            var bsIn = ByteOrderMark.UTF16_BE + ByteString.FromString("one,two,three\n", Encoding.BigEndianUnicode);
            this.Invoking(t => t.ExpectBsInOut(bsIn, new[,] { { "one", "two", "three" } }))
                .ShouldThrowExactly<UnsupportedCharsetException>();
        }

        [Fact]
        public void Csv_parsing_with_Byte_Order_Mark_should_fail_for_UTF32_LE_BOM()
        {
            var bsIn = ByteOrderMark.UTF32_LE + ByteString.FromString("one,two,three\n", Encoding.UTF32);
            this.Invoking(t => t.ExpectBsInOut(bsIn, new[,] { { "one", "two", "three" } }))
                .ShouldThrowExactly<UnsupportedCharsetException>();
        }

        [Fact]
        public void Csv_parsing_with_Byte_Order_Mark_should_fail_for_UTF32_BE_BOM()
        {
            var bsIn = ByteOrderMark.UTF32_BE + ByteString.FromString("one,two,three\n", Encoding.UTF32);
            this.Invoking(t => t.ExpectBsInOut(bsIn, new[,] { { "one", "two", "three" } }))
                .ShouldThrowExactly<UnsupportedCharsetException>();
        }

        private void ExpectInOut(string strIn, 
            string[,] expected = null,
            byte delimiter = CsvParsing.Comma,
            byte quoteChar = CsvParsing.DoubleQuote,
            byte escapeChar = CsvParsing.Backslash,
            bool requireLineEnd = true)
        {
            ExpectBsInOut(ByteString.FromString(strIn), expected, delimiter, quoteChar, escapeChar, requireLineEnd);
        }

        private void ExpectBsInOut(ByteString bsIn, 
            string[,] expected = null,
            byte delimiter = CsvParsing.Comma,
            byte quoteChar = CsvParsing.DoubleQuote,
            byte escapeChar = CsvParsing.Backslash,
            bool requireLineEnd = true)
        {
            var parser = new CsvParser(delimiter, quoteChar, escapeChar);
            parser.Offer(bsIn);
            List<ByteString> res;

            if (expected != null)
            {
                for (int i = 0; i < expected.GetLength(0); ++i)
                {
                    res = parser.Poll(requireLineEnd);
                    res.Count.Should().Be(expected.GetLength(1));

                    for (int j = 0; j < expected.GetLength(1); ++j)
                    {
                        var resStr = res[j].DecodeString();
                        resStr.ShouldBeEquivalentTo(expected[i, j]);
                    }
                }
            }

            parser.Poll(true).ShouldBeEquivalentTo(null);
        }
    }
}
