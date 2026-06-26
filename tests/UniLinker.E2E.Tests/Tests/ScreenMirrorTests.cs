using FluentAssertions;
using Microsoft.Playwright;
using UniLinker.E2E.Tests.Fixtures;
using Xunit;

namespace UniLinker.E2E.Tests.Tests;

/// <summary>
/// E2E tests for the ScreenMirror feature.
/// Tests the flow: Browser -> SignalingServer -> ScreenMirrorPlugin -> Video stream
/// </summary>
public class ScreenMirrorTests : IClassFixture<AppFixture>, IAsyncLifetime
{
    private readonly AppFixture _appFixture;
    private readonly PlaywrightFixture _playwrightFixture;
    private IPage? _page;

    public ScreenMirrorTests(AppFixture appFixture)
    {
        _appFixture = appFixture;
        _playwrightFixture = new PlaywrightFixture();
    }

    public async Task InitializeAsync()
    {
        _page = await _playwrightFixture.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _playwrightFixture.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "ScreenMirror")]
    public async Task SM01_Browser_CanAccessHomepage()
    {
        // Arrange
        var url = $"{AppFixture.BaseUrl}/index.html";

        // Act
        await _page!.GotoAsync(url);

        // Assert
        var title = await _page.TitleAsync();
        title.Should().Contain("UniLinker");

        // Verify key elements exist
        var logoVisible = await _page.Locator(".logo").IsVisibleAsync();
        logoVisible.Should().BeTrue("Logo should be visible on homepage");

        var connectButtonVisible = await _page.Locator("#btnConnect").IsVisibleAsync();
        connectButtonVisible.Should().BeTrue("Connect button should be visible");
    }

    [Fact]
    [Trait("Category", "ScreenMirror")]
    public async Task SM02_Browser_CanConnectToHost()
    {
        // Arrange
        var url = $"{AppFixture.BaseUrl}/index.html";
        await _page!.GotoAsync(url);

        // Act - Fill host input with localhost and click connect
        await _page.Locator("#hostInput").FillAsync("localhost");
        await _page.Locator("#btnConnect").ClickAsync();

        // Wait for connection state change
        await _page.WaitForSelectorAsync(
            ".status-dot.connected",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        // Assert
        var statusText = await _page.Locator("#statusText").TextContentAsync();
        statusText.Should().Be("已连接");
    }

    [Fact]
    [Trait("Category", "ScreenMirror")]
    public async Task SM03_Browser_ReceivesVideoStream()
    {
        // Arrange
        var url = $"{AppFixture.BaseUrl}/index.html";
        await _page!.GotoAsync(url);

        // Act - Connect to the host
        await _page.Locator("#hostInput").FillAsync("localhost");
        await _page.Locator("#btnConnect").ClickAsync();

        // Wait for video element to appear and have a stream
        await _page.WaitForFunctionAsync(
            @"() => {
                const video = document.getElementById('remoteVideo');
                return video && video.style.display !== 'none' && video.srcObject !== null;
            }",
            new PageWaitForFunctionOptions { Timeout = 20000 });

        // Assert - Video element is visible
        var videoVisible = await _page.Locator("#remoteVideo").IsVisibleAsync();
        videoVisible.Should().BeTrue("Video element should be visible after connection");

        // Verify video is playing
        var isPlaying = await _page.EvaluateAsync<bool>(
            @"() => {
                const video = document.getElementById('remoteVideo');
                return video && video.readyState >= 2 && !video.paused;
            }");
        isPlaying.Should().BeTrue("Video should be playing (readyState >= 2)");
    }

    [Fact]
    [Trait("Category", "ScreenMirror")]
    public async Task SM04_Browser_CanDisconnect()
    {
        // Arrange
        var url = $"{AppFixture.BaseUrl}/index.html";
        await _page!.GotoAsync(url);

        // Connect first
        await _page.Locator("#hostInput").FillAsync("localhost");
        await _page.Locator("#btnConnect").ClickAsync();
        await _page.WaitForSelectorAsync(
            ".status-dot.connected",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        // Act - Click disconnect (button text changes to "断开")
        await _page.Locator("#btnConnect").ClickAsync();

        // Assert - Wait for disconnected state
        await _page.WaitForSelectorAsync(
            "#statusText >> text=已断开",
            new PageWaitForSelectorOptions { Timeout = 5000 });

        var statusText = await _page.Locator("#statusText").TextContentAsync();
        statusText.Should().Be("已断开");

        // Video should be hidden
        var videoVisible = await _page.Locator("#remoteVideo").IsVisibleAsync();
        videoVisible.Should().BeFalse("Video should be hidden after disconnect");
    }
}
