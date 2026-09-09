namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Metadata about a loaded plugin instance.
/// Returned by <see cref="IPluginContext.Self"/>.
/// </summary>
public class PluginInfo
{
    /// <summary>Unique plugin identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Semantic version string.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>File path to the plugin DLL assembly.</summary>
    public string AssemblyPath { get; init; } = string.Empty;

    /// <summary>Capabilities this plugin provides.</summary>
    public string[] Capabilities { get; init; } = [];
}
