namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Bitmask of permissions a plugin may require.
/// Plugins declare their needed permissions; the platform may prompt the user for consent.
/// </summary>
[Flags]
public enum PluginPermission
{
    /// <summary>No special permissions required.</summary>
    None = 0,

    /// <summary>Access to screen/window capture (e.g., Windows.Graphics.Capture).</summary>
    ScreenCapture = 1 << 0,

    /// <summary>Access to microphone or system audio capture.</summary>
    AudioCapture = 1 << 1,

    /// <summary>Read/write access to the local file system.</summary>
    FileSystem = 1 << 2,

    /// <summary>Read/write access to the system clipboard.</summary>
    Clipboard = 1 << 3,

    /// <summary>Synthesize keyboard/mouse input on the local machine.</summary>
    InputSimulation = 1 << 4,

    /// <summary>Outbound network access (e.g., WebRTC, HTTP signaling).</summary>
    Network = 1 << 5,
}
