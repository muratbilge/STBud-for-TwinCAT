using System;
using System.IO;
using System.Text;

namespace STFormatter.Discover;

internal sealed class DualLogger : IDisposable
{
    private readonly StreamWriter fileWriter;
    private readonly string logPath;
    private bool disposed;

    public DualLogger()
    {
        logPath = Path.Combine(Path.GetTempPath(), "STFormatter_Discover.log");
        fileWriter = new StreamWriter(logPath, true, Encoding.UTF8);
    }

    public string LogPath => logPath;

    public void WriteLine(string message)
    {
        Console.WriteLine(message);
        fileWriter.WriteLine(message);
    }

    public void WriteSection(string title)
    {
        var sep = new string('=', 80);
        var line = $"{sep}";
        Console.WriteLine(line);
        Console.WriteLine(title);
        Console.WriteLine(line);
        fileWriter.WriteLine(line);
        fileWriter.WriteLine(title);
        fileWriter.WriteLine(line);
    }

    public void WriteError(string message, Exception? ex = null)
    {
        var sb = new StringBuilder();
        sb.Append("[ERROR] ").Append(message);
        if (ex != null)
        {
            sb.Append(": ").Append(ex.GetType().Name).Append(" - ").Append(ex.Message);
            if (ex.InnerException != null)
            {
                sb.Append(" | Inner: ").Append(ex.InnerException.GetType().Name)
                  .Append(" - ").Append(ex.InnerException.Message);
            }
        }
        var line = sb.ToString();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(line);
        Console.ResetColor();
        fileWriter.WriteLine(line);
    }

    public void Flush()
    {
        fileWriter.Flush();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        fileWriter.Flush();
        fileWriter.Dispose();
    }
}