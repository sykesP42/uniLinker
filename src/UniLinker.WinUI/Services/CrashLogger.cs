using System.Text;

namespace UniLinker.WinUI.Services;

/// <summary>
/// Writes unhandled exceptions to %APPDATA%\uniLinker\crash.log so startup
/// failures in unpackaged WinUI apps are diagnosable without a debugger.
/// </summary>
public static class CrashLogger
{
    private static readonly object Gate = new();
    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "crash.log");

    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            // Only fatal-ish categories to avoid log spam; XAML stowed
            // exceptions surface here before the process dies.
            if (e.Exception is System.IO.FileNotFoundException
                or System.Runtime.InteropServices.COMException
                or System.TypeInitializationException
                or System.DllNotFoundException
                or System.EntryPointNotFoundException)
            {
                Write("FirstChance", e.Exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Write("UnobservedTaskException", e.Exception);
    }

    public static void Write(string source, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] =====");
            sb.AppendLine(ex?.ToString() ?? "(no exception object)");
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch
        {
            // Last resort: never crash the crash logger.
        }
    }
}
