using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Streams.Dsl;
using Akka.IO;

namespace Akka.Streams.Csv.Dsl
{
    public static class CsvParsing
    {
        public const byte Backslash = 0x5c;   // '\\'
        public const byte Comma = 0x2c;       // ','
        public const byte SemiColon = 0x3b;   // ';'
        public const byte Colon = 0x3a;       // ':'
        public const byte Tab = 0x09;         // '\t'
        public const byte DoubleQuote = 0x22; // '"'

        /// <summary>
        /// Creates CSV parsing flow that reads CSV lines from incoming <see cref="Akka.IO.ByteString"/> objects.
        /// </summary>
        /// <param name="delimiter"></param>
        /// <param name="quoteChar"></param>
        /// <param name="escapeChar"></param>
        /// <returns></returns>
        public static Flow<ByteString, ImmutableList<ByteString>, NotUsed> LineScanner(
            byte delimiter = Comma,
            byte quoteChar = DoubleQuote,
            byte escapeChar = Backslash
        ) => Flow.FromGraph(new CsvParsingStage(delimiter, quoteChar, escapeChar));
    }
}
