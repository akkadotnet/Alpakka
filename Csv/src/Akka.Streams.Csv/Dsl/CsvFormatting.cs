using System.Collections.Immutable;
using System.Text;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Csv.Dsl
{
    /// <summary>
    /// Provides CSV formatting flows that convert a sequence of String into their CSV representation
    /// in <see cref="Akka.IO.ByteString"/>.
    /// </summary>
    public static class CsvFormatting
    {
        public const char Backslash = '\\';
        public const char Comma = ',';
        public const char SemiColon = ';';
        public const char Colon = ':';
        public const char Tab = '\t';
        public const char DoubleQuote = '"';

        /// <summary>
        /// Create a Flow for converting IImmutableList&lt;string&gt; to ByteString.
        /// </summary>
        /// <param name="delimiter"></param>
        /// <param name="quoteChar"></param>
        /// <param name="escapeChar"></param>
        /// <param name="endOfLine"></param>
        /// <param name="quotingStyle"></param>
        /// <param name="encoding"></param>
        /// <param name="byteOrderMark"></param>
        public static Flow<ImmutableList<string>, ByteString, NotUsed> Format(
            char delimiter = Comma, 
            char quoteChar = DoubleQuote, 
            char escapeChar = Backslash, 
            string endOfLine = "\r\n", 
            CsvQuotingStyle quotingStyle = CsvQuotingStyle.Required, 
            Encoding encoding = null,
            ByteString byteOrderMark = null)
        {
            var formatter = new CsvFormatter(delimiter, quoteChar, escapeChar, endOfLine, quotingStyle, encoding);

            if (byteOrderMark == null)
            {
                return Flow.FromFunction<ImmutableList<string>, ByteString>(list => formatter.ToCsv(list))
                    .Named("CsvFormatting");
            }

            return Flow.FromFunction<ImmutableList<string>, ByteString>(list => formatter.ToCsv(list))
                .Prepend(Source.Single(byteOrderMark))
                .Named("CsvFormatting");
        }
    }
}
