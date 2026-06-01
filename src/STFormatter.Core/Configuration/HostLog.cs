using System;

namespace STFormatter.Core.Configuration
{
    public static class HostLog
    {
        public const string FileName = "STFormatter_Host.log";

        public static string Path => System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            FileName);

        public static void Append(string source, string message)
        {
            try
            {
                string prefix = string.IsNullOrEmpty(source) ? "" : source + ": ";
                System.IO.File.AppendAllText(Path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {prefix}{message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
