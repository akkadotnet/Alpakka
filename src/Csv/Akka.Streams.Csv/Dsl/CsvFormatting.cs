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
        /// Create a Flow for converting <see cref="IImmutableList{String}"/> to ByteString.
        /// </summary>
        /// <param name="delimiter">Value delimiter, defaults to comma</param>
        /// <param name="quoteChar">Quote character, defaults to double quote</param>
        /// <param name="escapeChar">Escape character, defaults to backslash</param>
        /// <param name="endOfLine">Line ending (default CR, LF)</param>
        /// <param name="quotingStyle">Quote all fields, or only fields requiring quotes (default)</param>
        /// <param name="encoding">Character encoding, defaults to UTF-8</param>
        /// <param name="byteOrderMark">Certain CSV readers (namely Microsoft Excel) require a Byte Order mark, defaults to None</param>
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
                .Named("CsvFormatting")
                .Prepend(Source.Single(byteOrderMark));
        }
    }
}
