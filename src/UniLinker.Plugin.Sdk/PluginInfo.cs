namespace UniLinker.Plugin.Sdk;

public class PluginInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string AssemblyPath { get; init; } = string.Empty;
    public string[] Capabilities { get; init; } = [];
}
