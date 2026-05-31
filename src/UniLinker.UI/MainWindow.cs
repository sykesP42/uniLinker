using Microsoft.Web.WebView2.WinForms;

namespace UniLinker.UI;

public class MainWindow : Form
{
    private readonly WebView2 _webView;
    private readonly WebBridge _bridge;

    public MainWindow(WebBridge bridge)
    {
        _bridge = bridge;
        Text = "UniLinker";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 600);

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        Load += async (_, _) => await InitializeWebView();
        FormClosing += (_, e) =>
        {
            _webView.Dispose();
        };
    }

    private async Task InitializeWebView()
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "uniLinker", "WebView2");

        var env = await Microsoft.Web.WebView2.Core
            .CoreWebView2Environment.CreateAsync(null, userData);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.AddHostObjectToScript("bridge",
            new CoreWebView2Bridge(_bridge));

        // Try to load the web frontend
        var webPath = Path.Combine(AppContext.BaseDirectory, "web", "index.html");
        if (File.Exists(webPath))
        {
            _webView.CoreWebView2.Navigate(new Uri(webPath).AbsoluteUri);
        }
        else
        {
            // Dev fallback: navigate up from bin/
            var devPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "web", "index.html"));
            if (File.Exists(devPath))
                _webView.CoreWebView2.Navigate(new Uri(devPath).AbsoluteUri);
            else
                _webView.CoreWebView2.NavigateToString("<h1>Web files not found</h1>");
        }

        _webView.CoreWebView2.Settings.IsScriptEnabled = true;
    }
}
