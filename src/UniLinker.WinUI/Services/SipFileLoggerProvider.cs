using Microsoft.Extensions.Logging;
using System.Linq;
using System.Reflection;

namespace UniLinker.WinUI.Services;

/// <summary>
/// Minimal MEL provider that appends SIPSorcery's internal logs (ICE state,
/// STUN checks, SDP) to a file - the WinUI app has no console, so without
/// this every diagnostic the library writes disappears.
/// </summary>
public sealed class SipFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public SipFileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new SipFileLogger(categoryName, _path, _gate);

    public void Dispose() { }

    /// <summary>
    /// Inject this provider's logger directly into SIPSorcery's internal static
    /// Log (the MEL static-factory routing does not reach it in a WinUI app).
    /// Uses reflection because SIPSorcery.Sys.Log is internal.
    /// </summary>
    public void InjectIntoSipsorcery()
    {
        try
        {
            var logType = Type.GetType("SIPSorcery.Sys.Log, SIPSorcery")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("SIPSorcery.Sys.Log"))
                    .FirstOrDefault(t => t != null);
            if (logType == null) return;
            var logger = CreateLogger("sipsorcery");
            logType.GetProperty("Logger", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, logger);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SIPSorcery log injection failed: {ex.Message}");
        }
    }

    private sealed class SipFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private readonly object _gate;

        public SipFileLogger(string category, string path, object gate)
        {
            _category = category;
            _path = path;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {_category}: {formatter(state, exception)}";
                if (exception != null) line += Environment.NewLine + exception;
                lock (_gate)
                {
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never crash the app on logging.
            }
        }
    }
}
