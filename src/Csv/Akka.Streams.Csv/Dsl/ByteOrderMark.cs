using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;

namespace Akka.Streams.Csv.Dsl
{
    /// <summary>
    /// Byte Order Marks may be used to indicate the used character encoding in text files.
    /// @see http://www.unicode.org/faq/utf_bom.html#bom1
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "UTF, BE, and LE are acronyms")]
    public class ByteOrderMark
    {
        /// <summary>
        /// Byte Order Mark for UTF-16 big-endian
        /// </summary>
        public static readonly ByteString UTF16_BE;

        /// <summary>
        /// Byte Order Mark for UTF-16 little-endian
        /// </summary>
        public static readonly ByteString UTF16_LE;

        /// <summary>
        /// Byte Order Mark for UTF-32 big-endian
        /// </summary>
        public static readonly ByteString UTF32_BE;

        /// <summary>
        /// Byte Order Mark for UTF-32 little-endian
        /// </summary>
        public static readonly ByteString UTF32_LE;

        /// <summary>
        /// Byte Order Mark for UTF-8
        /// </summary>
        public static readonly ByteString UTF8;

        public static readonly ByteString None;

        static ByteOrderMark()
        {
            var ZeroZero = ByteString.FromBytes(new byte[]{0x00, 0x00});
            UTF16_BE = ByteString.FromBytes(new byte[] { 0xfe, 0xff });
            UTF16_LE = ByteString.FromBytes(new byte[] { 0xff, 0xfe });
            UTF32_BE = ZeroZero + UTF16_BE;
            UTF32_LE = UTF16_LE + ZeroZero;
            UTF8 = ByteString.FromBytes(new byte[]{0xEF, 0xBB, 0xBF});
            None = ByteString.Empty;
        }
    }
}
