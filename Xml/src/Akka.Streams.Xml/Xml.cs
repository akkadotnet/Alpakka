using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Stage;
using Akka.Util.Internal;

namespace Akka.Streams.Xml
{
    #region ParseEvent
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
        public CData(string text):base(text)
        {
        }
    }
    #endregion

    /// <summary>
    /// Internal API. Use <see cref="Akka.Streams.Xml.Dsl.XmlParsing"/> instead.
    /// </summary>
    public sealed class StreamingXmlParser:GraphStage<FlowShape<ByteString, IParseEvent>>
    {
        #region Logic
        private class Logic:InAndOutGraphStageLogic
        {
            private readonly BlockingStream _feeder;
            private readonly XmlReader _parser;

            private readonly StreamingXmlParser _stage;

            private IParseEvent _pendingEvent = null;
            private bool _hasNext = false;

            public Logic(StreamingXmlParser stage) : base(stage.Shape)
            {
                _stage = stage;

                _feeder = new BlockingStream();
                _parser = XmlReader.Create(_feeder, new XmlReaderSettings
                {
                    IgnoreComments = false,
                    IgnoreProcessingInstructions = false,
                    IgnoreWhitespace = true,
                    CloseInput = false,
                    Async = true
                });

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                var array = Grab(_stage.In).ToArray();
                _feeder.Write(array, 0, array.Length);
                AdvanceParser();
            }

            public override void OnPull()
            {
                // Check for empty buffer
                if (_feeder.Length == 0 && !IsClosed(_stage.In))
                {
                    Pull(_stage.In);
                    return;
                }
                AdvanceParser();
            }

            public override void OnUpstreamFinish()
            {
                _feeder.EndOfInput();
                if (!_hasNext)
                    CompleteStage();
                else if (IsAvailable(_stage.Out))
                    AdvanceParser();
            }

            private void AdvanceParser()
            {
                // Check for pending events
                if (_pendingEvent != null)
                {
                    Push(_stage.Out, _pendingEvent);
                    if (_pendingEvent is EndDocument)
                    {
                        CompleteStage();
                    }
                    _pendingEvent = null;
                    return;
                }

                try
                {
                    _hasNext = _parser.Read();
                }
                catch (XmlException e)
                {
                    FailStage(e);
                }

                if (!_hasNext)
                {
                    if (!_parser.EOF)
                    {
                        if (!IsClosed(_stage.In))
                        {
                            // EVENT_INCOMPLETE not supported, if this happens, the world blew up
                            _parser.Close();
                            FailStage(new IllegalStateException("Buffer underrun or multiple document exist inside the stream."));
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

                        if (_parser.Depth == 0)
                        {
                            // START_DOCUMENT
                            Push(_stage.Out, new StartDocument());
                            _pendingEvent = new StartElement(_parser.LocalName, attributes);
                            return;
                        }
                        Push(_stage.Out, new StartElement(_parser.LocalName, attributes));
                        break;

                    // END_ELEMENT
                    case XmlNodeType.EndElement:            
                        Push(_stage.Out, _parser.LocalName);
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
                    default:
                        AdvanceParser();
                        break;
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

        private class BlockingStream : MemoryStream
        {
            private readonly ManualResetEvent _dataReady = new ManualResetEvent(false);

            private bool _eoi;

            public void EndOfInput()
            {
                _eoi = true;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_eoi)
                {
                    throw new Exception("");
                }

                var oldPos = Position;
                Position = Length;
                base.Write(buffer, offset, count);
                Flush();
                Position = oldPos;

                _dataReady.Set();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Length - Position <= 4 && !_eoi)
                {
                    _dataReady.Reset();
                    _dataReady.WaitOne();
                }

                return base.Read(buffer, offset, count);
            }
        }
    }

    /// <summary>
    /// Internal API. Use <see cref="Akka.Streams.Xml.Dsl.XmlParsing"/> instead.
    /// </summary>
    public sealed class Coalesce : GraphStage<FlowShape<IParseEvent, IParseEvent>>
    {
        #region Logic
        private class Logic:InAndOutGraphStageLogic
        {
            private readonly Coalesce _stage;
            private bool _isBuffering = false;
            private StringBuilder _buffer = new StringBuilder();

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

    }

    /// <summary>
    /// Internal API. Use <see cref="Akka.Streams.Xml.Dsl.XmlParsing"/> instead.
    /// </summary>
    public sealed class Subslice : GraphStage<FlowShape<IParseEvent, IParseEvent>>
    {
        #region Logic
        private class Logic:InAndOutGraphStageLogic
        {
            private enum MatchState
            {
                Passthrough,
                PartialMatch,
                NoMatch
            }

            private readonly Subslice _stage;
            private List<string> _expected;
            private List<string> _matchedSoFar = new List<string>();
            private MatchState state;

            private int _passthroughDepth;
            private int _noMatchDepth;

            public Logic(Subslice stage, List<string> path ):base(stage.Shape)
            {
                _stage = stage;
                _expected = path;

                if(path == null || path.Count == 0)
                    state = MatchState.Passthrough;
                else
                    state = MatchState.PartialMatch;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                Pull(_stage.In);
            }

            public override void OnPush()
            {
                IParseEvent parseEvent = Grab(_stage.In);
                StartElement start;
                EndElement end;

                switch (state)
                {
                    case MatchState.Passthrough:

                        start = parseEvent as StartElement;
                        if (start != null)
                        {
                            _passthroughDepth++;
                            Push(_stage.Out, start);
                            return;
                        }

                        end = parseEvent as EndElement;
                        if (end != null)
                        {
                            if (_passthroughDepth == 0)
                            {
                                if (_matchedSoFar.Count > 0)
                                {
                                    _expected = new List<string> {_matchedSoFar[0]};
                                    _matchedSoFar.RemoveAt(0);
                                    state = MatchState.PartialMatch;
                                    Pull(_stage.In);
                                }
                            }
                            else
                            {
                                _passthroughDepth--;
                                Push(_stage.Out, end);
                            }
                            return;
                        }

                        Push(_stage.Out, parseEvent);
                        break;

                    case MatchState.PartialMatch:
                        parseEvent = Grab(_stage.In);

                        start = parseEvent as StartElement;
                        if (start != null)
                        {
                            if (start.LocalName == _expected.Head())
                            {
                                _matchedSoFar.Insert(0, _expected.Head());
                                _expected.RemoveAt(0);
                                if (_expected.Count == 0)
                                    state = MatchState.Passthrough;
                            }
                            else
                                state = MatchState.NoMatch;
                            Pull(_stage.In);
                            return;
                        }

                        end = parseEvent as EndElement;
                        if (end != null)
                        {
                            _expected.Insert(0, _matchedSoFar.Head());
                            _matchedSoFar.RemoveAt(0);
                            Pull(_stage.In);
                            return;
                        }
                        Pull(_stage.In);
                        break;

                    case MatchState.NoMatch:
                        start = parseEvent as StartElement;
                        if (start != null)
                        {
                            _noMatchDepth++;
                            Pull(_stage.In);
                            return;
                        }

                        end = parseEvent as EndElement;
                        if (end != null)
                        {
                            if (_noMatchDepth == 0)
                                state = MatchState.PartialMatch;
                            else
                                _noMatchDepth--;
                            Pull(_stage.In);
                            return;
                        }

                        Pull(_stage.In);
                        break;
                }
            }
        }

        #endregion

        public Subslice(ImmutableList<string> path)
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
    }
}
