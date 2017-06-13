using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Akka.IO;
using Akka.Streams.Csv.Dsl;

namespace Akka.Streams.Csv
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class CsvFormatter
    {
        private readonly char _delimiter;

        private readonly char _quoteChar;
        private readonly string _duplicatedQuote;

        private readonly char _escapeChar;
        private readonly string _duplicatedEscape;

        private readonly char[] _quoteOrEscapechar;
        private readonly char[] _needQuoteChars;

        private readonly string _endOfLine;
        private readonly ByteString _endOfLineBs;

        private readonly CsvQuotingStyle _quotingStyle;
        private readonly Encoding _encoding;

        public string CharsetName => _encoding.EncodingName;

        public CsvFormatter(char delimiter, char quoteChar, char escapeChar, string endOfLine, CsvQuotingStyle quotingStyle, Encoding encoding)
        {
            _delimiter = delimiter;

            _quoteChar = quoteChar;
            _duplicatedQuote = new string(quoteChar, 2);

            _escapeChar = escapeChar;
            _duplicatedEscape = new string(escapeChar, 2);

            _quoteOrEscapechar = new[] {quoteChar, escapeChar};
            _needQuoteChars = new[] {'\r', '\n', delimiter};

            _endOfLine = endOfLine;
            _endOfLineBs = ByteString.FromString(_endOfLine);

            _quotingStyle = quotingStyle;
            _encoding = encoding;
        }

        
        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        public ByteString ToCsv(IEnumerable fields)
        {
            if (fields == null)
            {
                return _endOfLineBs;
            }

            if (fields.GetEnumerator().MoveNext())
            {
                if (_encoding == null)
                {
                    return ByteString.FromString(NonEmptyToCsv(fields));
                }
                return ByteString.FromString(NonEmptyToCsv(fields), _encoding);
            }

            return _endOfLineBs;
        }

        private string NonEmptyToCsv(IEnumerable fields)
        {
            var builder = new StringBuilder();

            void SplitAndDuplicateQuotesAndEscapes(string field, int splitAt)
            {
                var lastIndex = 0;
                var index = splitAt;
                while (index > -1)
                {
                    builder.Append(field.Substring(lastIndex, index - lastIndex));
                    builder.Append(field[index] == _quoteChar ? _duplicatedQuote : _duplicatedEscape);
                    lastIndex = index + 1;
                    index = field.IndexOfAny(_quoteOrEscapechar, lastIndex);
                }
                if (lastIndex < field.Length)
                {
                    builder.Append(field.Substring(lastIndex));
                }
            }

            void Append(string field)
            {
                int splitAt;
                var quoteIt = RequiresQuoteOrSplit(field, out splitAt);
                if (quoteIt)
                {
                    builder.Append(_quoteChar);
                    if (splitAt != -1)
                        SplitAndDuplicateQuotesAndEscapes(field, splitAt);
                    else
                        builder.Append(field);
                    builder.Append(_quoteChar);
                }
                else
                {
                    builder.Append(field);
                }
            }

            var iterator = fields.GetEnumerator();
            var hasNext = iterator.MoveNext();
            var value = iterator.Current;
            while (hasNext)
            {
                if (value != null)
                {
                    Append(value.ToString());
                }
                hasNext = iterator.MoveNext();
                if (hasNext)
                {
                    builder.Append(_delimiter);
                }
            }
            builder.Append(_endOfLine);
            return builder.ToString();
        }

        private bool RequiresQuoteOrSplit(string field, out int splitIndex)
        {
            splitIndex = field.IndexOfAny(_quoteOrEscapechar);
            return CsvQuotingStyle.Always == _quotingStyle || splitIndex != -1 || field.IndexOfAny(_needQuoteChars) != -1;
        }
    }
}
