using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;

namespace Akka.Streams.Csv.Dsl
{
    public static class CsvToDictionary
    {
        /// <summary>
        /// A flow translating incoming <see cref="ImmutableList{ByteString}"/> to <see cref="Dictionary{String, ByteString}"/> 
        /// using the streams first element's values as keys.
        /// </summary>
        /// <param name="encoding">the encoding to decode <see cref="ByteString"/> to <see cref="string"/>, defaults to <see cref="Encoding.UTF8"/></param>
        /// <returns></returns>
        public static Flow<ImmutableList<ByteString>, Dictionary<string, ByteString>, NotUsed> ToDictionary(Encoding encoding = null)
        {
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }

            return Flow.FromGraph(new CsvToDictionaryStage(null, encoding));
        }

        /// <summary>
        /// A flow translating incoming <see cref="ImmutableList{ByteString}"/> to <see cref="Dictionary{String, ByteString}"/> 
        /// using the given headers as keys.
        /// </summary>
        /// <param name="headers">column names to be used as dictionary keys</param>
        /// <returns></returns>
        public static Flow<ImmutableList<ByteString>, Dictionary<string, ByteString>, NotUsed> WithHeaders(ImmutableList<string> headers)
        {
            if(headers == null)
                throw new ArgumentException($"{nameof(headers)} was null.", nameof(headers));

            return Flow.FromGraph(new CsvToDictionaryStage(headers, Encoding.UTF8));
        }
    }
}
