using System;

namespace STFormatter.Core.Configuration
{
    public static class HostLog
    {
        public const string FileName = "STBud_Host.log";

        // Rotate once past this size: STBud_Host.log -> STBud_Host.log.1 (single generation).
        // Keeps the log bounded without losing the most recent history.
        private const long MaxBytes = 5 * 1024 * 1024;

        private static readonly object _gate = new object();

        public static string Path => System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            FileName);

        public static void Append(string source, string message)
        {
            try
            {
                string prefix = string.IsNullOrEmpty(source) ? "" : source + ": ";
                lock (_gate)
                {
                    RotateIfNeeded();
                    System.IO.File.AppendAllText(Path,
                        $"[{DateTime.Now:HH:mm:ss.fff}] {prefix}{message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // A logger must never throw; surface the failure to an attached debugger only.
                System.Diagnostics.Debug.WriteLine($"HostLog write failed: {ex.Message}");
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var info = new System.IO.FileInfo(Path);
                if (!info.Exists || info.Length < MaxBytes) return;
                string backup = Path + ".1";
                if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
                System.IO.File.Move(Path, backup);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HostLog rotate failed: {ex.Message}");
            }
        }
    }
}
