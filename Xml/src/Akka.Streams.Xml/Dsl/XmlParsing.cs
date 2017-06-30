using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Xml.Dsl
{
    public static class XmlParsing
    {
        /// <summary>
        /// Parser Flow that takes a stream of ByteStrings and parses them to XML events similar to SAX.
        /// </summary>
        /// <returns></returns>
        public static Flow<ByteString, IParseEvent, NotUsed> Parser()
            => Flow.FromGraph(new StreamingXmlParser());

        /// <summary>
        /// A Flow that transforms a stream of XML ParseEvents. This stage coalesces consequitive CData
        /// events into a single Characters event or fails if the buffered string is larger than the maximum defined.
        /// </summary>
        /// <param name="maximumTextLength">The maximum number of consecutive CData events to be coalesced</param>
        /// <returns></returns>
        public static Flow<IParseEvent, IParseEvent, NotUsed> Coalesce(int maximumTextLength)
            => Flow.FromGraph(new Coalesce(maximumTextLength));

        /// <summary>
        /// A Flow that transforms a stream of XML ParseEvents. This stage filters out any event not corresponding to
        /// a certain path in the XML document. Any event that is under the specified path (including subpaths) is passed
        /// through.
        /// </summary>
        /// <param name="path">A list of path, starting from the root to the deepest tag, to be filtered</param>
        /// <returns></returns>
        public static Flow<IParseEvent, IParseEvent, NotUsed> Subslice(IImmutableList<string> path)
            => Flow.FromGraph(new Subslice(path));
    }
}
