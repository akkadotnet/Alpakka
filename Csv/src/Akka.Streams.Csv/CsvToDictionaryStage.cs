using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Stage;

namespace Akka.Streams.Csv
{
    /// <summary>
    /// Internal API: Converts incoming <see cref="ImmutableList{ByteString}"/> to <see cref="Dictionary{string, ByteString}"/>
    /// </summary>
    internal class CsvToDictionaryStage:GraphStage<FlowShape<ImmutableList<ByteString>, Dictionary<string, ByteString>>>
    {
        #region Logic
        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly CsvToDictionaryStage _stage;
            private ImmutableList<string> _headers;
            private readonly Encoding _encoding;

            public Logic(CsvToDictionaryStage stage, ImmutableList<string> columnNames, Encoding encoding) : base(stage.Shape)
            {
                _stage = stage;
                _headers = columnNames;
                _encoding = encoding;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                var elements = Grab(_stage.In);
                if (_headers != null)
                {
                    var map = new Dictionary<string, ByteString>();
                    var len = Math.Min(_headers.Count, elements.Count);
                    for(int i = 0; i < len; ++i)
                    {
                        map.Add(_headers[i], elements[i]);
                    }
                    Push(_stage.Out, map);
                }
                else
                {
                    var headers = new List<string>();
                    foreach (var elem in elements)
                    {
                        headers.Add(elem.DecodeString(_encoding));
                    }
                    _headers = headers.ToImmutableList();
                    Pull(_stage.In);
                }
            }

            public override void OnPull()
            {
                Pull(_stage.In);
            }

        }
        #endregion

        private readonly ImmutableList<string> _columnNames;
        private readonly Encoding _encoding;

        /// <summary>
        /// Internal API: Converts incoming <see cref="ImmutableList{ByteString}"/> to <see cref="Dictionary{string, ByteString}"/>
        /// </summary>
        /// <param name="columnNames">If given, these names are used as map keys; if not first stream element is used</param>
        /// <param name="encoding">Character encoding used to convert header line ByteString to String</param>
        public CsvToDictionaryStage(ImmutableList<string> columnNames, Encoding encoding)
        {
            _columnNames = columnNames;
            _encoding = encoding;

            Shape = new FlowShape<ImmutableList<ByteString>, Dictionary<string, ByteString>>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("CsvToDictionary");

        internal Inlet<ImmutableList<ByteString>> In { get; } = new Inlet<ImmutableList<ByteString>>("CsvToDictionary.in");
        internal Outlet<Dictionary<string, ByteString>> Out { get; } = new Outlet<Dictionary<string, ByteString>>("CsvToDictionary.out");

        public override FlowShape<ImmutableList<ByteString>, Dictionary<string, ByteString>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, _columnNames, _encoding);

        public override string ToString() => nameof(CsvToDictionaryStage);
    }
}
