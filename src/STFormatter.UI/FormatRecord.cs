using System;
using STFormatter.Core.Text;

namespace STFormatter.UI
{
    public class FormatRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string FilePath { get; set; } = "";
        public string Section { get; set; } = "";
        public string OriginalText { get; set; } = "";
        public string FormattedText { get; set; } = "";
        public int Pid { get; set; }
        public string Title { get; set; } = "";
        public bool Success { get; set; }
        public string Method { get; set; } = "";

        public string FileName => string.IsNullOrEmpty(FilePath)
            ? ""
            : System.IO.Path.GetFileName(FilePath);

        public int OriginalLineCountValue => LineCounter.Count(OriginalText);

        public int FormattedLineCountValue => LineCounter.Count(FormattedText);

        public string OriginalLineCount => OriginalLineCountValue.ToString();

        public string FormattedLineCount => FormattedLineCountValue.ToString();

        public string Summary =>
            $"{Timestamp:HH:mm:ss} | {FileName} | {Section} | {OriginalLineCountValue}\u2192{FormattedLineCountValue} lines | {(Success ? "OK" : "FAIL")}";

        public bool HasDiff => OriginalText != FormattedText && !(string.IsNullOrEmpty(OriginalText) && string.IsNullOrEmpty(FormattedText));
    }
}
