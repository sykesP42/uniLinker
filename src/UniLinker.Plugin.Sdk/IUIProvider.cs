namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Platform-specific UI registration. Plugins use this to add panels and tray actions.
/// The actual UI implementation varies by platform (WinUI, Compose, etc.).
/// </summary>
public interface IUIProvider
{
    /// <summary>Register a UI panel for this plugin (e.g., a settings page or control view).</summary>
    void RegisterPanel(string pluginId, PanelInfo panel);

    /// <summary>Register a system tray action for this plugin.</summary>
    void RegisterTrayAction(string pluginId, TrayAction action);
}

/// <summary>Metadata for a plugin's UI panel.</summary>
public class PanelInfo
{
    /// <summary>Panel title shown in the UI.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Icon identifier.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Path to the HTML file that renders this panel (for WebView-based UIs).</summary>
    public string HtmlPath { get; init; } = string.Empty;
}

/// <summary>Metadata for a system tray context menu action.</summary>
public class TrayAction
{
    /// <summary>Action label shown in the tray menu.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Callback invoked when the action is clicked.</summary>
    public Action? OnClick { get; init; }
}
