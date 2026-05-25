using System;

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
        public bool Success { get; set; }
        public string Method { get; set; } = "";

        public string FileName => string.IsNullOrEmpty(FilePath)
            ? ""
            : System.IO.Path.GetFileName(FilePath);

        public string OriginalLineCount
        {
            get
            {
                if (string.IsNullOrEmpty(OriginalText)) return "0";
                return OriginalText.Split('\n').Length.ToString();
            }
        }

        public string FormattedLineCount
        {
            get
            {
                if (string.IsNullOrEmpty(FormattedText)) return "0";
                return FormattedText.Split('\n').Length.ToString();
            }
        }

        public string Summary =>
            $"{Timestamp:HH:mm:ss} | {FileName} | {Section} | {OriginalLineCount}→{FormattedLineCount} lines | {(Success ? "OK" : "FAIL")}";
    }
}