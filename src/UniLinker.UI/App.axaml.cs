using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System.Diagnostics;
using UniLinker.Core;
using UniLinker.UI.ViewModels;
using UniLinker.UI.Views;

namespace UniLinker.UI;

public class App : Application
{
    public static Platform? Platform { get; private set; }
    public static WebBridge? Bridge { get; private set; }
    public static SignalingServer? SignalingServer { get; private set; }

    public static void InitializeServices(Platform platform, WebBridge bridge, SignalingServer signalingServer)
    {
        Platform = platform;
        Bridge = bridge;
        SignalingServer = signalingServer;

        // Start services immediately
        StartServicesAsync();
    }

    private static async void StartServicesAsync()
    {
        try
        {
            Debug.WriteLine("[UniLinker] Starting platform...");
            await Platform!.StartAsync();
            Debug.WriteLine("[UniLinker] Platform started");

            Debug.WriteLine("[UniLinker] Starting signaling server on port 9527...");
            _ = SignalingServer!.StartAsync();
            Debug.WriteLine("[UniLinker] Signaling server started");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UniLinker] Error starting services: {ex.Message}");
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LoadThemeResources(ThemeVariant.Dark);
    }

    private void LoadThemeResources(ThemeVariant variant)
    {
        Resources.MergedDictionaries.Clear();

        var themeUrl = variant == ThemeVariant.Light
            ? "avares://UniLinker.UI/Styles/ThemeLight.axaml"
            : "avares://UniLinker.UI/Styles/ThemeDark.axaml";

        var themeResources = AvaloniaXamlLoader.Load(new Uri(themeUrl)) as ResourceDictionary;
        if (themeResources != null)
        {
            Resources.MergedDictionaries.Add(themeResources);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(Bridge!);
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow.Closing += async (_, _) =>
            {
                SignalingServer?.Dispose();
                await Platform!.StopAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void SetTheme(ThemeVariant variant)
    {
        RequestedThemeVariant = variant;
        LoadThemeResources(variant);
    }
}