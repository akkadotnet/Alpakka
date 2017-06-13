using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Csv.Dsl
{
    /// <summary>
    /// Provides CSV formatting flows that convert a sequence of String into their CSV representation
    /// in <see cref="Akka.IO.ByteString"/>.
    /// </summary>
    public class CsvFormatting
    {
        private const char _backslash = '\\';
        private const char _comma = ',';
        private const char _semiColon = ';';
        private const char _colon = ':';
        private const char _tab = '\t';
        private const char _doubleQuote = '"';

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
        public Flow<IImmutableList<string>, ByteString, NotUsed> Format(
            char delimiter = _comma, 
            char quoteChar = _doubleQuote, 
            char escapeChar = _backslash, 
            string endOfLine = "\r\n", 
            CsvQuotingStyle quotingStyle = CsvQuotingStyle.Required, 
            Encoding encoding = null,
            ByteString byteOrderMark = null)
        {
            var formatter = new CsvFormatter(delimiter, quoteChar, escapeChar, endOfLine, quotingStyle, encoding);

            if (byteOrderMark == null)
            {
                return Flow.FromFunction<IImmutableList<string>, ByteString>(list => formatter.ToCsv(list))
                    .Named("CsvFormatting");
            }

            return Flow.FromFunction<IImmutableList<string>, ByteString>(list => formatter.ToCsv(list))
                .Prepend(Source.Single(byteOrderMark))
                .Named("CsvFormatting");
        }
    }
}
