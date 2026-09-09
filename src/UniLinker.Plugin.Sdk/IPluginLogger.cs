namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Structured logging interface for plugins.
/// Implementations route log output to the platform's logging infrastructure.
/// </summary>
public interface IPluginLogger
{
    /// <summary>Log a debug-level message (verbose, development-only).</summary>
    void Debug(string message);

    /// <summary>Log an informational message (normal operation).</summary>
    void Info(string message);

    /// <summary>Log a warning (unexpected but non-fatal condition).</summary>
    void Warn(string message);

    /// <summary>Log an error with optional exception details.</summary>
    void Error(string message, Exception? ex = null);
}
