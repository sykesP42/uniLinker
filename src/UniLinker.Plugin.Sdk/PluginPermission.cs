namespace UniLinker.Plugin.Sdk;

[Flags]
public enum PluginPermission
{
    None = 0,
    ScreenCapture = 1 << 0,
    AudioCapture = 1 << 1,
    FileSystem = 1 << 2,
    Clipboard = 1 << 3,
    InputSimulation = 1 << 4,
    Network = 1 << 5,
}
