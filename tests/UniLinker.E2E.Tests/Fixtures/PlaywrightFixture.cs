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

        _browser = _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--disable-gpu", "--disable-software-rasterizer" }
        }).GetAwaiter().GetResult();

        Console.WriteLine("[PlaywrightFixture] Browser launched");
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
