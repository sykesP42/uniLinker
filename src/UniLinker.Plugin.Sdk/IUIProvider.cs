namespace UniLinker.Plugin.Sdk;

public interface IUIProvider
{
    void RegisterPanel(string pluginId, PanelInfo panel);
    void RegisterTrayAction(string pluginId, TrayAction action);
}

public class PanelInfo
{
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string HtmlPath { get; init; } = string.Empty;
}

public class TrayAction
{
    public string Label { get; init; } = string.Empty;
    public Action? OnClick { get; init; }
}
