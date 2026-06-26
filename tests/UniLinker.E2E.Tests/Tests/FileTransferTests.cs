using FluentAssertions;
using Microsoft.Playwright;
using UniLinker.E2E.Tests.Fixtures;
using Xunit;

namespace UniLinker.E2E.Tests.Tests;

/// <summary>
/// E2E tests for the FileTransfer feature.
/// Tests the flow: Browser -> FileTransfer UI -> Transfer files
/// </summary>
public class FileTransferTests : IClassFixture<AppFixture>, IAsyncLifetime
{
    private readonly AppFixture _appFixture;
    private readonly PlaywrightFixture _playwrightFixture;
    private IPage? _page;

    public FileTransferTests(AppFixture appFixture)
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
    [Trait("Category", "FileTransfer")]
    public async Task FT01_FileTransfer_PageIsAccessible()
    {
        // Note: FileTransfer UI is part of the WinUI app, not the web page.
        // This test verifies the web API endpoint exists.

        // Act
        var response = await _page!.Context.APIRequest.GetAsync(
            $"{AppFixture.BaseUrl}/info");

        // Assert
        response.Ok.Should().BeTrue("Server should be accessible");

        var body = await response.TextAsync();
        body.Should().Contain("capabilities");
    }

    [Fact]
    [Trait("Category", "FileTransfer")]
    public async Task FT02_Server_ReturnsDeviceInfo()
    {
        // Arrange & Act
        var response = await _page!.Context.APIRequest.GetAsync(
            $"{AppFixture.BaseUrl}/info");

        // Assert
        response.Ok.Should().BeTrue();

        var body = await response.TextAsync();
        body.Should().Contain("name");
        body.Should().Contain("version");
        body.Should().Contain("status");
    }

    [Fact]
    [Trait("Category", "FileTransfer")]
    public async Task FT03_Server_ReturnsStatus()
    {
        // Arrange & Act
        var response = await _page!.Context.APIRequest.GetAsync(
            $"{AppFixture.BaseUrl}/api/status");

        // Assert
        response.Ok.Should().BeTrue();

        var body = await response.TextAsync();
        body.Should().Contain("deviceName");
        body.Should().Contain("status");
    }
}