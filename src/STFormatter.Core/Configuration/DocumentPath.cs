using System;

namespace STFormatter.Core.Configuration
{
    /// <summary>
    /// Normalizes document paths reported by the TcXaeShell DTE. When a POU's
    /// method/action/property editor tab is active, <c>ActiveDocument.FullName</c> is a
    /// pseudo-path with the member appended after a semicolon:
    /// <c>C:\...\FB_Sample.TcPOU;FB_Sample.MyMethod</c>. Treating that as a literal file
    /// path breaks everything downstream (File.Exists, git repo resolution, disk writes).
    /// </summary>
    public static class DocumentPath
    {
        /// <summary>Strip a ";POU.Member" suffix so the result is the real file path.</summary>
        public static string Normalize(string? path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
            int i = path!.IndexOf(';');
            return i < 0 ? path : path.Substring(0, i);
        }
    }
}
