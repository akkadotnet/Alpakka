using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Akka.Event;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Stage;
using Akka.Util.Internal;
using AsyncCallback = System.AsyncCallback;

namespace Akka.Streams.Xml
{
    #region Parsing event messages
    /**
     * XML parsing events emitted by the parser flow. These roughly emulates Java XMLEvent types.
     */
    public interface IParseEvent
    {
    }

    public abstract class TextEvent : IParseEvent
    {
        public string Text { get; }

        protected TextEvent(string text)
        {
            Text = text;
        }
    }

    public sealed class StartDocument : IParseEvent
    {
    }

    public sealed class EndDocument : IParseEvent
    {
    }

    public sealed class StartElement : IParseEvent
    {
        public string LocalName { get; }
        public Dictionary<string, string> Attributes { get; }

        public StartElement(string localName, Dictionary<string, string> attributes)
        {
            LocalName = localName;
            Attributes = attributes;
        }
    }

    public sealed class EndElement : IParseEvent
    {
        public string LocalName { get; }

        public EndElement(string localName)
        {
            LocalName = localName;
        }
    }

    public sealed class Characters : TextEvent
    {
        public Characters(string text) : base(text)
        {
        }
    }

    public sealed class ProcessingInstruction : IParseEvent
    {
        public string Target { get; }
        public string Data { get; }

        public ProcessingInstruction(string target, string data)
        {
            Target = target;
            Data = data;
        }
    }

    public sealed class Comment : IParseEvent
    {
        public string Text { get; }

        public Comment(string text)
        {
            Text = text;
        }
    }

    public sealed class CData : TextEvent
    {
        public CData(string text) : base(text)
        {
        }
    }
    #endregion

    #region StreamingXmlParser
    /// <summary>
    /// Internal API. Use <see cref="Akka.Streams.Xml.Dsl.XmlParsing"/> instead.
    /// </summary>
    public sealed class StreamingXmlParser : GraphStage<FlowShape<ByteString, IParseEvent>>
    {
        #region Logic
        private class Logic : InAndOutGraphStageLogic
        {
            private readonly MemoryStream _feeder;
            private readonly StreamingXmlParser _stage;
            private readonly int _bufferSize;

            private XmlReader _parser;
            private IParseEvent _pendingEvent;
            private bool _hasNext = true;
            private bool _documentStarted;
            private bool _hasBeenPushed;
            private ByteString _overflowBuffer = ByteString.Empty;

            private bool DataAvailable => !_overflowBuffer.IsEmpty || !IsClosed(_stage.In);

            public Logic(StreamingXmlParser stage, int bufferSize) : base(stage.Shape)
            {
                _stage = stage;
                _bufferSize = bufferSize;
                _feeder = new MemoryStream(new byte[_bufferSize], true);
                _feeder.Position = _feeder.Length;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                // Fix for janky condition where XMLParser requires initial seed data inside the stream in order to work at all
                if (!_hasBeenPushed)
                {
                    if(!HasBeenPulled(_stage.In))
                        Pull(_stage.In);
                    return;
                }
                AdvanceParser();
            }

            public override void OnPush()
            {
                _hasBeenPushed = true;

                var bytes = Grab(_stage.In);

                if (!_overflowBuffer.IsEmpty)
                {
                    // There's overflown data leftover from previous operations, append it to the current data
                    bytes = _overflowBuffer.Concat(bytes);
                    _overflowBuffer = ByteString.Empty;
                }

                if (_feeder.Position != _feeder.Length)
                {
                    // There's data in the buffer, so append the new data to the old data
                    var oldBytes = new byte[_feeder.Length - _feeder.Position];
                    _feeder.Read(oldBytes, 0, oldBytes.Length);
                    bytes = ByteString.FromByteBuffer(new ByteBuffer(oldBytes)).Concat(bytes);
                }

                FillStreamBuffer(bytes);
                AdvanceParser();
            }

            private void GetNextStreamBuffer()
            {
                if (!_overflowBuffer.IsEmpty)
                {
                    // Overflow buffer isn't empty, so copy it into the buffer instead of asking for more upstream data
                    var sliceLen = Math.Min(_overflowBuffer.Count, _bufferSize);
                    var bytes = _overflowBuffer.Slice(0, sliceLen);
                    _overflowBuffer = _overflowBuffer.Drop(sliceLen);
                    FillStreamBuffer(bytes);
                    AdvanceParser();
                    return;
                }

                Pull(_stage.In);
            }

            private void FillStreamBuffer(ByteString bytes)
            {
                if (bytes.Count > _bufferSize)
                {
                    // Incoming data is too big for the current buffer size, truncate and save the overflow.
                    _overflowBuffer = bytes.Drop(_bufferSize);
                    bytes = bytes.Slice(0, _bufferSize);
                }

                // Copy the data to the memory stream. 
                // XmlParser reads data from buffer position to the end,
                // thats why we offset the data so it fits into the right side of the buffer.
                var offset = _bufferSize - bytes.Count;
                _feeder.Position = offset;
                _feeder.Write(bytes.ToArray(), 0, bytes.Count);
                _feeder.Position = offset;
            }

            public override void OnUpstreamFinish()
            {
                if (_hasNext)
                {
                    if(_pendingEvent == null)
                        AdvanceParser();
                }
                else
                    CompleteStage();
            }

            private void AdvanceParser()
            {
                // Check for pending events
                if (_pendingEvent != null)
                {
                    Push(_stage.Out, _pendingEvent);
                    if (_pendingEvent is EndDocument)
                        _documentStarted = false;
                    _pendingEvent = null;
                    return;
                }

                // Check for empty buffer condition. 
                // XmlParser requires that there are at least 6 characters in the stream, 
                // or it will read the stream multiple times to get more data,
                // which will result in premature EOF in our case.
                if (_feeder.Length - _feeder.Position < 7 && DataAvailable)
                {
                    GetNextStreamBuffer();
                    return;
                }

                if (_parser == null)
                    _parser = XmlReader.Create(_feeder, new XmlReaderSettings
                    {
                        IgnoreComments = false,
                        IgnoreProcessingInstructions = false,
                        IgnoreWhitespace = true,
                        CloseInput = false,
                        ConformanceLevel = ConformanceLevel.Fragment
                    });

                try
                {
                    _hasNext = _parser.Read();
                }
                catch (Exception e)
                {
                    FailStage(e);
                    return;
                }

                if (!_hasNext)
                {
                    if (!_parser.EOF)
                    {
                        if (IsClosed(_stage.In))
                        {
                            _parser.Close();
                            FailStage(new IllegalStateException("Stream finished before event was fully parsed."));
                        }
                        else
                        {
                            _parser.Close();
                            FailStage(new IllegalStateException("Unknown error occured. Parsing finished before stream was finished."));
                        }
                    }
                    CompleteStage();
                    return;
                }

                switch (_parser.NodeType)
                {
                    // START_ELEMENT
                    case XmlNodeType.Element:
                        var attributes = new Dictionary<string, string>();
                        while (_parser.MoveToNextAttribute())
                        {
                            attributes.Add(_parser.LocalName, _parser.Value);
                        }
                        _parser.MoveToElement();

                        if (_parser.Depth == 0 && !_documentStarted)
                        {
                            // START_DOCUMENT
                            _documentStarted = true;
                            Push(_stage.Out, new StartDocument());
                            _pendingEvent = new StartElement(_parser.LocalName, attributes);
                        }
                        else
                            Push(_stage.Out, new StartElement(_parser.LocalName, attributes));
                        break;

                    // END_ELEMENT
                    case XmlNodeType.EndElement:
                        Push(_stage.Out, new EndElement(_parser.LocalName));
                        if (_parser.Depth == 0)
                        {
                            // END_DOCUMENT
                            _pendingEvent = new EndDocument();
                        }
                        break;

                    // CHARACTERS
                    case XmlNodeType.Text:
                        Push(_stage.Out, new Characters(_parser.Value));
                        break;

                    // PROCESSING_INSTRUCTION
                    case XmlNodeType.ProcessingInstruction:
                        if (_parser.Depth == 0 && !_documentStarted)
                        {
                            // START_DOCUMENT
                            _documentStarted = true;
                            Push(_stage.Out, new StartDocument());
                            _pendingEvent = new ProcessingInstruction(_parser.Name, _parser.Value);
                        }
                        else
                            Push(_stage.Out, new ProcessingInstruction(_parser.Name, _parser.Value));
                        break;

                    // COMMENT
                    case XmlNodeType.Comment:
                        Push(_stage.Out, new Comment(_parser.Value));
                        break;

                    // CDATA
                    case XmlNodeType.CDATA:
                        Push(_stage.Out, new CData(_parser.Value));
                        break;

                    // Do not support DTD, SPACE, NAMESPACE, NOTATION_DECLARATION, ENTITY_DECLARATION, PROCESSING_INSTRUCTION
                    // ATTRIBUTE is handled in START_ELEMENT implicitly
                    // EVENT_INCOMPLETE is handled directly in AsyncXmlStream
                    default:
                        if (_feeder.Length - _feeder.Position < 7 && DataAvailable)
                        {
                            GetNextStreamBuffer();
                            return;
                        }
                        AdvanceParser();
                        return;
                }
            }
        }
        #endregion

        public StreamingXmlParser(int bufferSize)
        {
            if (bufferSize < 64)
                throw new ArgumentException($"Buffer size must be greater than 64 (was:{bufferSize})");

            Shape = new FlowShape<ByteString, IParseEvent>(In, Out);
            _bufferSize = bufferSize;
        }

        private readonly int _bufferSize;

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("XmlParser");

        public Inlet<ByteString> In { get; } = new Inlet<ByteString>("XMLParser.In");
        public Outlet<IParseEvent> Out { get; } = new Outlet<IParseEvent>("XMLParser.out");

        public override FlowShape<ByteString, IParseEvent> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, _bufferSize);

        public override string ToString() => nameof(StreamingXmlParser);
    }
    #endregion

    #region Coalesce
    /// <summary>
    /// Internal API. Use <see cref="Akka.Streams.Xml.Dsl.XmlParsing"/> instead.
    /// </summary>
    public sealed class Coalesce : GraphStage<FlowShape<IParseEvent, IParseEvent>>
    {
        #region Logic
        private class Logic : InAndOutGraphStageLogic
        {
            private readonly Coalesce _stage;
            private bool _isBuffering = false;
            private readonly StringBuilder _buffer = new StringBuilder();

            public Logic(Coalesce stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                Pull(_stage.In);
            }

            public override void OnPush()
            {
                var parseEvent = Grab(_stage.In);

                var t = parseEvent as TextEvent;
                if (t != null)
                {
                    if (t.Text.Length + _buffer.Length > _stage.MaximumTextLength)
                        FailStage(new IllegalStateException($"Too long character sequence, maximum is {_stage.MaximumTextLength} but got {t.Text.Length + _buffer.Length - _stage.MaximumTextLength} more "));
                    else
                    {
                        _buffer.Append(t.Text);
                        _isBuffering = true;
                        Pull(_stage.In);
                    }
                }
                else
                {
                    if (_isBuffering)
                    {
                        _isBuffering = false;
                        var coalesced = _buffer.ToString();
                        _buffer.Clear();
                        Emit(_stage.Out, new Characters(coalesced), () =>
                        {
                            Emit(_stage.Out, parseEvent, () =>
                            {
                                if (IsClosed(_stage.In))
                                    CompleteStage();
                            });
                        });
                    }
                    else
                        Push(_stage.Out, parseEvent);
                }
            }

            public override void OnUpstreamFinish()
            {
                if (_isBuffering)
                    Emit(_stage.Out, new Characters(_buffer.ToString()), CompleteStage);
                else
                    CompleteStage();
            }
        }
        #endregion

        public Coalesce(int maximumTextLength)
        {
            Shape = new FlowShape<IParseEvent, IParseEvent>(In, Out);
            MaximumTextLength = maximumTextLength;
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("XMLCoalesce");

        public int MaximumTextLength { get; }
        public Inlet<IParseEvent> In { get; } = new Inlet<IParseEvent>("XMLCoalesce.In");
        public Outlet<IParseEvent> Out { get; } = new Outlet<IParseEvent>("XMLCoalesce.out");

        public override FlowShape<IParseEvent, IParseEvent> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        public override string ToString() => nameof(Coalesce);
    }
    #endregion

    #region Subslice
    /// <summary>
    /// Internal API. Use <see cref="Akka.Streams.Xml.Dsl.XmlParsing"/> instead.
    /// </summary>
    public sealed class Subslice : GraphStage<FlowShape<IParseEvent, IParseEvent>>
    {
        #region Logic
        private class Logic : InAndOutGraphStageLogic
        {
            private enum MatchState
            {
                Passthrough,
                PartialMatch,
                NoMatch
            }

            private readonly Subslice _stage;
            private readonly Lazy<PassThrough> _passThrough;
            private readonly Lazy<PartialMatch> _partialMatch;
            private readonly Lazy<NoMatch> _noMatch;

            private MatchState State
            {
                set
                {
                    switch (value)
                    {
                        case MatchState.Passthrough:
                            SetHandler(_stage.In, _passThrough.Value);
                            break;
                        case MatchState.PartialMatch:
                            SetHandler(_stage.In, _partialMatch.Value);
                            break;
                        case MatchState.NoMatch:
                            SetHandler(_stage.In, _noMatch.Value);
                            break;
                    }
                }
            }
            private Stack<string> Expected { get; set; }
            private Stack<string> MatchedSoFar { get; } = new Stack<string>();

            public Logic(Subslice stage, List<string> path) : base(stage.Shape)
            {
                _stage = stage;
                _passThrough = new Lazy<PassThrough>(() => new PassThrough(stage, this));
                _partialMatch = new Lazy<PartialMatch>(() => new PartialMatch(stage, this));
                _noMatch = new Lazy<NoMatch>(() => new NoMatch(stage, this));

                if (path != null)
                {
                    path.Reverse();
                    Expected = new Stack<string>(path);
                }
                else
                {
                    Expected = new Stack<string>();
                }

                State = Expected.Count == 0 ? MatchState.Passthrough : MatchState.PartialMatch;

                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                Pull(_stage.In);
            }

            public override void OnPush()
            {
                throw new NotImplementedException("Execution should never reach this execution path, ever.");
            }

            private class PassThrough : InHandler
            {
                private readonly Subslice _stage;
                private readonly Logic _logic;
                private int _depth;

                public PassThrough(Subslice stage, Logic logic)
                {
                    _stage = stage;
                    _logic = logic;
                }

                public override void OnPush()
                {
                    var inEvent = _logic.Grab(_stage.In);

                    var start = inEvent as StartElement;
                    if (start != null)
                    {
                        _depth++;
                        _logic.Push(_stage.Out, start);
                        return;
                    }

                    var end = inEvent as EndElement;
                    if (end != null)
                    {
                        if (_depth == 0)
                        {
                            _logic.Expected.Push(_logic.MatchedSoFar.Pop());
                            _logic.State = MatchState.PartialMatch;
                            _logic.Pull(_stage.In);
                        }
                        else
                        {
                            _depth--;
                            _logic.Push(_stage.Out, end);
                        }
                        return;
                    }

                    _logic.Push(_stage.Out, inEvent);
                }
            }

            private class PartialMatch : InHandler
            {
                private readonly Subslice _stage;
                private readonly Logic _logic;

                public PartialMatch(Subslice stage, Logic logic)
                {
                    _stage = stage;
                    _logic = logic;
                }

                public override void OnPush()
                {
                    var inEvent = _logic.Grab(_stage.In);

                    var start = inEvent as StartElement;
                    if (start != null)
                    {
                        if (start.LocalName == _logic.Expected.Head())
                        {
                            _logic.MatchedSoFar.Push(_logic.Expected.Pop());
                            if (_logic.Expected.Count == 0)
                            {
                                _logic.State = MatchState.Passthrough;
                            }
                        }
                        else
                        {
                            _logic.State = MatchState.NoMatch;
                        }
                        _logic.Pull(_stage.In);
                        return;
                    }

                    var end = inEvent as EndElement;
                    if (end != null)
                    {
                        _logic.Expected.Push(_logic.MatchedSoFar.Pop());
                        _logic.Pull(_stage.In);
                        return;
                    }

                    _logic.Pull(_stage.In);
                }
            }

            private class NoMatch : InHandler
            {
                private readonly Subslice _stage;
                private readonly Logic _logic;
                private int _depth;

                public NoMatch(Subslice stage, Logic logic)
                {
                    _stage = stage;
                    _logic = logic;
                }

                public override void OnPush()
                {
                    var inEvent = _logic.Grab(_stage.In);

                    var start = inEvent as StartElement;
                    if (start != null)
                    {
                        _depth++;
                        _logic.Pull(_stage.In);
                        return;
                    }

                    var end = inEvent as EndElement;
                    if (end != null)
                    {
                        if(_depth == 0)
                            _logic.State = MatchState.PartialMatch;
                        else
                            _depth--;

                        _logic.Pull(_stage.In);
                        return;
                    }

                    _logic.Pull(_stage.In);
                }
            }
        }

        #endregion

        public Subslice(IImmutableList<string> path)
        {
            Shape = new FlowShape<IParseEvent, IParseEvent>(In, Out);
            Path = path.ToList();
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("XMLSubslice");

        public List<string> Path { get; }
        public Inlet<IParseEvent> In { get; } = new Inlet<IParseEvent>("XMLSubslice.In");
        public Outlet<IParseEvent> Out { get; } = new Outlet<IParseEvent>("XMLSubslice.out");

        public override FlowShape<IParseEvent, IParseEvent> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, Path);

        public override string ToString() => nameof(Subslice);
    }
    #endregion

}

