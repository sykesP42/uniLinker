namespace UniLinker.Core;

/// <summary>
/// Lightweight file + debug logging for non-plugin components.
/// Writes to %APPDATA%\uniLinker\unilinker.log (same file as plugin logs).
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "uniLinker", "unilinker.log");

    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg);
    public static void Error(string msg) => Write("ERR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
