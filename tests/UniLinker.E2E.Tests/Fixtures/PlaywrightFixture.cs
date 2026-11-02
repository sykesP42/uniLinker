using Microsoft.Playwright;

namespace UniLinker.E2E.Tests.Fixtures;

/// <summary>
/// Manages Playwright browser instance for E2E testing.
/// </summary>
public class PlaywrightFixture : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly List<IPage> _pages = new();

    public IBrowser Browser => _browser;

    public PlaywrightFixture()
    {
        Console.WriteLine("[PlaywrightFixture] Initializing Playwright...");

        _playwright = Microsoft.Playwright.Playwright.CreateAsync().GetAwaiter().GetResult();

        // Prefer the installed Edge/Chrome channel: the Playwright Chromium
        // build is open-source and has NO H264 codec, so the screen mirror
        // flow cannot be tested on it. Real browsers ship H264.
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--disable-gpu", "--disable-software-rasterizer", "--autoplay-policy=no-user-gesture-required" },
        };
        foreach (var channel in new[] { "msedge", "chrome" })
        {
            try
            {
                launchOptions.Channel = channel;
                _browser = _playwright.Chromium.LaunchAsync(launchOptions).GetAwaiter().GetResult();
                Console.WriteLine($"[PlaywrightFixture] Browser launched (channel: {channel})");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PlaywrightFixture] channel {channel} unavailable: {ex.Message.Split('\n')[0]}");
                _browser = null!;
            }
        }
        if (_browser == null)
        {
            launchOptions.Channel = null;
            _browser = _playwright.Chromium.LaunchAsync(launchOptions).GetAwaiter().GetResult();
            Console.WriteLine("[PlaywrightFixture] Browser launched (bundled chromium; H264 tests will fail)");
        }
    }

    public async Task<IPage> NewPageAsync()
    {
        var page = await _browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });

        _pages.Add(page);
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        Console.WriteLine("[PlaywrightFixture] Disposing...");

        foreach (var page in _pages)
        {
            try
            {
                await page.CloseAsync();
            }
            catch { /* ignore */ }
        }

        try
        {
            await _browser.CloseAsync();
        }
        catch { /* ignore */ }

        _playwright.Dispose();
    }
}
