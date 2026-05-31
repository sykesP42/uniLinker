namespace UniLinker.Core;

public static class ErrorHandler
{
    public static void SetupGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log($"[FATAL] {ex?.Message}\n{ex?.StackTrace}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"[WARN] Unobserved task: {args.Exception?.Message}");
            args.SetObserved();
        };
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "uniLinker", "error.log");
            var dir = Path.GetDirectoryName(logPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { /* best effort */ }
    }
}
