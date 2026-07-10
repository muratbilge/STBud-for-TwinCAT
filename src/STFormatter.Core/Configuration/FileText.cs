using System;
using System.Text;

namespace STFormatter.Core.Configuration
{
    /// <summary>
    /// Encoding-preserving text file reading. TwinCAT writes .TcPOU/.TcDUT/.TcGVL with a
    /// UTF-8 BOM; writing them back BOM-less (the .NET default) churns every file against
    /// TcXaeShell's own output. Read the text and hand back an encoding that reproduces
    /// the original preamble on write.
    /// </summary>
    public static class FileText
    {
        public static string ReadPreservingEncoding(string path, out Encoding encoding)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            return DecodePreservingEncoding(bytes, out encoding);
        }

        /// <summary>Decode <paramref name="bytes"/>, returning an encoding whose preamble
        /// matches the input's BOM (UTF-8 BOM, UTF-16 LE, or BOM-less UTF-8).</summary>
        public static string DecodePreservingEncoding(byte[] bytes, out Encoding encoding)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            encoding = new UTF8Encoding(false);
            return encoding.GetString(bytes);
        }
    }
}
