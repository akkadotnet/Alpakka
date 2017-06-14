using System;
using System.Collections.Generic;
using Akka.IO;
using Akka.Streams.Csv.Dsl;

namespace Akka.Streams.Csv
{
    /// <summary>
    /// INTERNAL API: Use <see cref="Akka.Streams.Csv.Dsl.CsvParsing"/> instead.
    /// </summary>
    public sealed class CsvParser
    {
        private enum State
        {
            LineStart,
            WithinField,
            AfterDelimiter,
            LineEnd,
            QuoteStarted,
            QuoteEnd,
            WithinQuotedField
        }

        private const byte Lf = 0x0c; // '\n'
        private const byte Cr = 0x0f; // '\r'

        private readonly byte _delimiter;
        private readonly byte _quoteChar;
        private readonly byte _escapeChar;

        private ByteString _buffer = ByteString.Empty;
        private bool _firstData = true;
        private long _currentLineNo;

        public int Pos { get; private set; }
        public int FieldStart { get; private set; }

        /// <summary>
        /// INTERNAL API: Use <see cref="Akka.Streams.Csv.Dsl.CsvParsing"/> instead.
        /// </summary>
        /// <param name="delimiter"></param>
        /// <param name="quoteChar"></param>
        /// <param name="escapeChar"></param>
        public CsvParser(byte delimiter, byte quoteChar, byte escapeChar)
        {
            _delimiter = delimiter;
            _quoteChar = quoteChar;
            _escapeChar = escapeChar;
        }

        public void Offer(ByteString input)
        {
            _buffer += input;
        }

        public List<ByteString> Poll(bool requireLineEnd)
        {
            if (_buffer.NonEmpty)
            {
                var preFirstData = _firstData;
                var prePos = Pos;
                var preSieldStart = FieldStart;
                var line = ParseLine(requireLineEnd);
                if (line.Count > 0)
                {
                    _currentLineNo++;
                    DropReadBuffer();
                }
                else
                {
                    _firstData = preFirstData;
                    Pos = prePos;
                    FieldStart = preSieldStart;
                }
                return line;
            }
            return null;
        }

        private void DropReadBuffer()
        {
            _buffer.Drop(Pos);
            Pos = 0;
            FieldStart = 0;
        }

        // FieldBuilder will just cut the required part out of the incoming ByteBuffer
        // as long as non escaping is used.
        private sealed class FieldBuilder
        {
            private CsvParser _parser;
            private ByteString _buf;
            private bool _useBuilder;
            private ByteStringBuilder _builder;

            public FieldBuilder(ByteString buf, CsvParser parser)
            {
                _buf = buf;
                _parser = parser;
            }

            // Set up the ByteString builder instead of relying on `ByteString.slice`.
            public void Init(byte x)
            {
                if (!_useBuilder)
                {
                    _builder = ByteString.NewBuilder().Append(_buf.Slice(_parser.FieldStart, _parser.Pos)) + x;
                    _useBuilder = true;
                }
                else
                {
                    _builder += x;
                }
            }

            public void Add(byte x)
            {
                if (_useBuilder)
                    _builder += x;
            }

            public ByteString Result(int pos)
            {
                if (_useBuilder)
                {
                    _useBuilder = false;
                    return _builder.Result();
                }
                return _buf.Slice(_parser.FieldStart, pos);
            }
        }

        // TODO: Check exception handling. Original scala exception handling might be faulty?, it returns exceptions when it is parsing, but disregards any csv errors when doing return checks at the end.
        private List<ByteString> ParseLine(bool requireLineEnd)
        {
            var buf = _buffer;
            var columns = new List<ByteString>();
            var state = State.LineStart;
            var fieldBuilder = new FieldBuilder(buf, this);

            void WrongCharEscaped()
            {
                throw new MalformedCsvException($"wrong escaping at {_currentLineNo}:{Pos}, only escape or delimiter may be escaped.");
            }

            void WrongCharEscapedWithinQuotes()
            {
                throw new MalformedCsvException($"wrong escaping at {_currentLineNo}:{Pos}, only escape or quote may be escaped within quotes.");
            }

            void NoCharEscaped()
            {
                throw new MalformedCsvException($"wrong escaping at {_currentLineNo}:{Pos}, no character after escape.");
            }

            void ReadPastLf()
            {
                if (Pos < buf.Count && buf[Pos] == Lf)
                {
                    Pos++;
                }
            }

            void CheckForByteOrderMark()
            {
                if (buf.Count >= 2)
                {
                    if (buf.StartsWith(ByteOrderMark.UTF8))
                    {
                        Pos = 3;
                        FieldStart = 3;
                    }
                    else
                    {
                        if (buf.StartsWith(ByteOrderMark.UTF16_LE))
                            throw new UnsupportedCharsetException("UTF-16 LE and UTF-32 LE");
                        if (buf.StartsWith(ByteOrderMark.UTF16_BE))
                            throw new UnsupportedCharsetException("UTF-16 BE");
                        if (buf.StartsWith(ByteOrderMark.UTF32_BE))
                            throw new UnsupportedCharsetException("UTF-32 BE");
                    }
                }
            }

            if (_firstData)
            {
                CheckForByteOrderMark();
                _firstData = false;
            }

            while (state != State.LineEnd && Pos < buf.Count)
            {
                var b = buf[Pos];
                switch (state)
                {
                    case State.LineStart:
                        if (b == _quoteChar)
                        {
                            state = State.QuoteStarted;
                            Pos++;
                            FieldStart = Pos;
                            continue;
                        }

                        if (b == _delimiter)
                        {
                            columns.Add(ByteString.Empty);
                            state = State.AfterDelimiter;
                            Pos++;
                            FieldStart = Pos;
                            continue;
                        }

                        switch (b)
                        {
                            case Lf:
                                columns.Add(ByteString.Empty);
                                state = State.LineEnd;
                                Pos++;
                                FieldStart = Pos;
                                break;
                            case Cr:
                                columns.Add(ByteString.Empty);
                                state = State.LineEnd;
                                Pos++;
                                ReadPastLf();
                                FieldStart = Pos;
                                break;
                            default:
                                fieldBuilder.Add(b);
                                state = State.WithinField;
                                Pos++;
                                break;
                        }
                        break;

                    case State.AfterDelimiter:
                        if (b == _quoteChar)
                        {
                            state = State.QuoteStarted;
                            Pos++;
                            FieldStart = Pos;
                            continue;
                        }

                        if (b == _escapeChar)
                        {
                            if (Pos + 1 >= buf.Count)
                                NoCharEscaped();

                            if (buf[Pos + 1] != _escapeChar && buf[Pos + 1] != _delimiter)
                                WrongCharEscaped();

                            fieldBuilder.Init(buf[Pos + 1]);
                            state = State.WithinField;
                            Pos += 2;
                            continue;
                        }

                        if (b == _delimiter)
                        {
                            columns.Add(ByteString.Empty);
                            state = State.AfterDelimiter;
                            Pos++;
                            FieldStart = Pos;
                            continue;
                        }

                        switch (b)
                        {
                            case Lf:
                                columns.Add(ByteString.Empty);
                                state = State.LineEnd;
                                Pos++;
                                FieldStart = Pos;
                                break;
                            case Cr:
                                columns.Add(ByteString.Empty);
                                state = State.LineEnd;
                                Pos++;
                                ReadPastLf();
                                FieldStart = Pos;
                                break;
                            default:
                                fieldBuilder.Add(b);
                                state = State.WithinField;
                                Pos++;
                                break;
                        }
                        break;

                    case State.WithinField:
                        if (b == _escapeChar)
                        {
                            if (Pos + 1 >= buf.Count)
                                NoCharEscaped();

                            if (buf[Pos + 1] != _escapeChar && buf[Pos + 1] != _delimiter)
                                WrongCharEscaped();

                            fieldBuilder.Init(buf[Pos + 1]);
                            state = State.WithinField;
                            Pos += 2;
                            continue;
                        }

                        if (b == _delimiter)
                        {
                            columns.Add(fieldBuilder.Result(Pos));
                            state = State.AfterDelimiter;
                            Pos++;
                            FieldStart = Pos;
                            continue;
                        }

                        switch (b)
                        {
                            case Lf:
                                columns.Add(fieldBuilder.Result(Pos));
                                state = State.LineEnd;
                                Pos++;
                                FieldStart = Pos;
                                break;
                            case Cr:
                                columns.Add(fieldBuilder.Result(Pos));
                                state = State.LineEnd;
                                Pos++;
                                ReadPastLf();
                                FieldStart = Pos;
                                break;
                            default:
                                fieldBuilder.Add(b);
                                state = State.WithinField;
                                Pos++;
                                break;
                        }
                        break;

                    case State.QuoteStarted:
                        if (b == _escapeChar && _escapeChar != _quoteChar)
                        {
                            if (Pos + 1 >= buf.Count)
                                NoCharEscaped();

                            if (buf[Pos + 1] != _escapeChar && buf[Pos + 1] != _quoteChar)
                                WrongCharEscapedWithinQuotes();

                            fieldBuilder.Init(buf[Pos + 1]);
                            state = State.WithinQuotedField;
                            Pos += 2;
                            continue;
                        }

                        if (b == _quoteChar)
                        {
                            if (Pos + 1 < buf.Count && buf[Pos + 1] == _quoteChar)
                            {
                                fieldBuilder.Init(b);
                                state = State.WithinQuotedField;
                                Pos += 2;
                                continue;
                            }
                            state = State.QuoteEnd;
                            Pos++;
                            continue;
                        }

                        fieldBuilder.Add(b);
                        state = State.WithinQuotedField;
                        Pos++;
                        break;

                    case State.QuoteEnd:
                        if (b == _delimiter)
                        {
                            columns.Add(fieldBuilder.Result(Pos - 1));
                            state = State.AfterDelimiter;
                            Pos++;
                            FieldStart = Pos;
                            continue;
                        }

                        switch (b)
                        {
                            case Lf:
                                columns.Add(fieldBuilder.Result(Pos - 1));
                                state = State.LineEnd;
                                Pos++;
                                FieldStart = Pos;
                                break;
                            case Cr:
                                columns.Add(fieldBuilder.Result(Pos - 1));
                                state = State.LineEnd;
                                Pos++;
                                ReadPastLf();
                                FieldStart = Pos;
                                break;
                            default:
                                throw new MalformedCsvException($"Expected delimiter or end of line at {_currentLineNo}:{Pos}");
                        }
                        break;

                    case State.WithinQuotedField:
                        if (b == _escapeChar && _escapeChar != _quoteChar)
                        {
                            if (Pos + 1 >= buf.Count)
                                NoCharEscaped();

                            if (buf[Pos + 1] != _escapeChar && buf[Pos + 1] != _quoteChar)
                                WrongCharEscapedWithinQuotes();

                            fieldBuilder.Init(buf[Pos + 1]);
                            state = State.WithinQuotedField;
                            Pos += 2;
                            continue;
                        }

                        if (b == _quoteChar)
                        {
                            if (Pos + 1 < buf.Count && buf[Pos + 1] == _quoteChar)
                            {
                                fieldBuilder.Init(b);
                                state = State.WithinQuotedField;
                                Pos += 2;
                                continue;
                            }
                            state = State.QuoteEnd;
                            Pos++;
                            continue;
                        }

                        fieldBuilder.Add(b);
                        state = State.WithinQuotedField;
                        Pos++;
                        break;
                }
            }

            if (requireLineEnd)
            {
                if (state == State.LineEnd)
                    return columns;
                return null;
            }

            switch (state)
            {
                case State.AfterDelimiter:
                    return columns;
                case State.WithinQuotedField:
                    return null;
                case State.WithinField:
                    columns.Add(fieldBuilder.Result(Pos));
                    return columns;
                case State.QuoteEnd:
                    columns.Add(fieldBuilder.Result(Pos - 1));
                    return columns;
            }

            return columns;
        }
    }


    public class MalformedCsvException : Exception
    {
        public MalformedCsvException() { }
        public MalformedCsvException(string message) : base(message) { }
        public MalformedCsvException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class UnsupportedCharsetException : Exception
    {
        public UnsupportedCharsetException() { }
        public UnsupportedCharsetException(string message) : base(message) { }
        public UnsupportedCharsetException(string message, Exception innerException) : base(message, innerException) { }
    }

    internal static class ByteStringExtension
    {
        public static bool StartsWith(this ByteString bs1, ByteString bs2)
        {
            if (bs1.Count < bs2.Count)
                return false;

            for (int i = 0; i < bs2.Count; ++i)
            {
                if (bs1[i] != bs2[i])
                    return false;
            }

            return true;
        }
    }
}
