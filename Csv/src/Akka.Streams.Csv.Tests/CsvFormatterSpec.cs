using System.Text;
using Akka.IO;
using Akka.Streams.Csv.Dsl;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Csv.Tests
{
    public class CsvFormatterSpec
    {
        [Fact]
        public void CsvFormatter_with_comma_delimiter_should_format_strings()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] {"ett", "två", "tre"}, "ett,två,tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_comma_delimiter_should_format_strings_containing_commas()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "t,vå", "tre" }, "ett,\"t,vå\",tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_comma_delimiter_should_format_strings_containing_quotes()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "t\"vå", "tre" }, "ett,\"t\"\"vå\",tre\r\n");
        }


        [Fact]
        public void CsvFormatter_quoting_everything_should_format_strings()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Always);
            ExpectInOut(formatter, new[] { "ett", "två", "tre" }, @"""ett"",""två"",""tre""" + "\r\n");
        }

        [Fact]
        public void CsvFormatter_quoting_everything_should_format_strings_with_commas()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Always);
            ExpectInOut(formatter, new[] { "ett", "t,vå", "tre" }, @"""ett"",""t,vå"",""tre""" + "\r\n");
        }

        [Fact]
        public void CsvFormatter_quoting_everything_should_format_strings_containing_quotes()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Always);
            ExpectInOut(formatter, new[] { "ett", "t\"vå", "tre" }, @"""ett"",""t""""vå"",""tre""" + "\r\n");
        }

        [Fact]
        public void CsvFormatter_quoting_everything_should_format_strings_containing_quotes_twice()
        {
            var formatter = new CsvFormatter(',', '"', '\\', "\r\n", CsvQuotingStyle.Always);
            ExpectInOut(formatter, new[] { "ett", "t\"v\"å", "tre" }, @"""ett"",""t""""v""""å"",""tre""" + "\r\n");
        }


        [Fact]
        public void CsvFormatter_with_required_quoting_should_format_strings()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "två", "tre" }, "ett;två;tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_quote_strings_with_delimiters()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "t;vå", "tre" }, "ett;\"t;vå\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_quote_strings_with_quotes()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "t\"vå", "tre" }, "ett;\"t\"\"vå\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_quote_strings_with_quotes_at_end()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "två\"", "tre" }, "ett;\"två\"\"\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_quote_strings_with_just_a_quote()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "\"", "tre" }, "ett;\"\"\"\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_quote_strings_containing_LF()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "\n", "tre" }, "ett;\"\n\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_quote_strings_containing_CR_LF()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "prefix\r\npostfix", "tre" }, "ett;\"prefix\r\npostfix\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_duplicate_escape_char()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "prefix\\postfix", "tre" }, "ett;\"prefix\\\\postfix\";tre\r\n");
        }

        [Fact]
        public void CsvFormatter_with_required_quoting_should_duplicate_escape_chars_and_quotes()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required);
            ExpectInOut(formatter, new[] { "ett", "one\\two\"three\\four", "tre" }, "ett;\"one\\\\two\"\"three\\\\four\";tre\r\n");
        }


        [Fact]
        public void CsvFormatter_with_non_standard_charset_should_get_the_encoding_right()
        {
            var formatter = new CsvFormatter(';', '"', '\\', "\r\n", CsvQuotingStyle.Required, Encoding.Unicode);
            var csv = formatter.ToCsv(new[] { "ett", "två", "อักษรไทย" });
            csv.ShouldBeEquivalentTo(ByteString.FromString("ett;två;อักษรไทย\r\n", Encoding.Unicode));
        }

        private void ExpectInOut(CsvFormatter formatter, string[] strIn, string strOut)
        {
            formatter.ToCsv(strIn).ShouldBeEquivalentTo(ByteString.FromString(strOut));
        }
    }
}
