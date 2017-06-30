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
            private readonly AsyncXmlStream _feeder;
            private readonly XmlReader _parser;
            private readonly StreamingXmlParser _stage;

            private IParseEvent _pendingEvent;
            private bool _hasNext = true;
            private bool _documentStarted;

            public Logic(StreamingXmlParser stage) : base(stage.Shape)
            {
                _stage = stage;
                _feeder = new AsyncXmlStream(stage, this);
                _parser = XmlReader.Create(_feeder, new XmlReaderSettings
                {
                    IgnoreComments = false,
                    IgnoreProcessingInstructions = false,
                    IgnoreWhitespace = true,
                    CloseInput = false,
                    Async = true
                });

                SetHandler(stage.In, _feeder);
                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                Task.WhenAll(AdvanceParser());
            }

            public override void OnPush()
            {
                throw new NotImplementedException("Execution should never reach this execution path, ever.");
            }

            private async Task AdvanceParser()
            {
                // Check for document start
                if (!_documentStarted)
                {
                    // START_DOCUMENT
                    _documentStarted = true;
                    Push(_stage.Out, new StartDocument());
                    return;
                }

                // Check for pending events
                if (_pendingEvent != null)
                {
                    Push(_stage.Out, _pendingEvent);
                    if (_pendingEvent is EndDocument)
                        CompleteStage();
                    _pendingEvent = null;
                    return;
                }

                try
                {
                    _hasNext = await _parser.ReadAsync();
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
                        if (!IsClosed(_stage.In))
                        {
                            _parser.Close();
                            FailStage(new IllegalStateException("Multiple documents in a single stream is not supported."));
                        }
                        else
                        {
                            _parser.Close();
                            FailStage(new IllegalStateException("Stream finished before event was fully parsed."));
                        }
                    }
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
                        await AdvanceParser();
                        return;
                }
            }

            private class AsyncXmlStream : Stream, IInHandler
            {
                private readonly Logic _logic;
                private readonly StreamingXmlParser _stage;
                private TaskCompletionSource<object> _dataReady;

                private byte[] _buffer;

                public AsyncXmlStream(StreamingXmlParser stage, Logic logic)
                {
                    _stage = stage;
                    _logic = logic;
                }

                public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                {
                    if (_dataReady != null)
                        throw new Exception("Can not start a new ReadAsync because there is an older operation pending.");

                    if (_buffer == null && !_logic.IsClosed(_stage.In))
                    {
                        _logic.Pull(_stage.In);
                        _dataReady = new TaskCompletionSource<object>();
                        await _dataReady.Task;
                    }

                    _dataReady = null;
                    return Read(buffer, offset, count);
                }

                public override int Read(byte[] buffer, int offset, int count)
                {
                    _dataReady?.Task.Wait();

                    if (_buffer == null)
                    {
                        return 0;
                    }

                    if (count > _buffer.Length)
                    {
                        var len = _buffer.Length;
                        Buffer.BlockCopy(_buffer, 0, buffer, offset, len);
                        _buffer = null;
                        return len;
                    }

                    Buffer.BlockCopy(_buffer, 0, buffer, offset, count);
                    var newBuffer = new byte[_buffer.Length - count];
                    Buffer.BlockCopy(_buffer, _buffer.Length, newBuffer, 0, count);
                    _buffer = newBuffer;
                    return count;
                }

                public void OnPush()
                {
                    var array = _logic.Grab(_stage.In).ToArray();

                    if (_buffer == null)
                    {
                        _buffer = array;
                    }
                    else
                    {
                        var newBuffer = new byte[_buffer.Length + array.Length];
                        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _buffer.Length);
                        Buffer.BlockCopy(array, 0, newBuffer, _buffer.Length, array.Length);
                        _buffer = newBuffer;
                    }

                    _dataReady?.SetResult(null);
                }

                public void OnUpstreamFinish()
                {
                    _dataReady?.SetResult(null);
                }

                public void OnUpstreamFailure(Exception e)
                {
                    _dataReady?.SetResult(null);
                    _logic.FailStage(e);
                }

                public override long Length => _buffer?.Length ?? 0;

                public override void Flush()
                {
                }

                public override void Close()
                {
                }


                public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                {
                    throw new NotSupportedException();
                }

                public override void Write(byte[] buffer, int offset, int count)
                {
                    throw new NotSupportedException();
                }

                public override long Seek(long offset, SeekOrigin origin)
                {
                    throw new NotSupportedException();
                }

                public override void SetLength(long value)
                {
                    throw new NotSupportedException();
                }

                public override bool CanRead => true;
                public override bool CanSeek => false;
                public override bool CanWrite => false;

                public override long Position
                {
                    get { throw new NotSupportedException(); }
                    set { throw new NotSupportedException(); }
                }
            }
        }
        #endregion

        public StreamingXmlParser()
        {
            Shape = new FlowShape<ByteString, IParseEvent>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("XmlParser");

        public Inlet<ByteString> In { get; } = new Inlet<ByteString>("XMLParser.In");
        public Outlet<IParseEvent> Out { get; } = new Outlet<IParseEvent>("XMLParser.out");

        public override FlowShape<ByteString, IParseEvent> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

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

