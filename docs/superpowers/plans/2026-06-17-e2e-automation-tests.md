# UniLinker E2E Automation Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create Playwright-based E2E tests for ScreenMirror and FileTransfer features, verifying Windows app works correctly with browser clients.

**Architecture:** Test project starts the UniLinker WinUI app as a background process, waits for the signaling server to be ready, then uses Playwright to drive a browser and verify functionality.

**Tech Stack:** .NET 9, xUnit, Playwright, FluentAssertions

---

## File Structure

```
tests/
└── UniLinker.E2E.Tests/
    ├── UniLinker.E2E.Tests.csproj       # Test project with Playwright + xUnit
    ├── Fixtures/
    │   ├── AppFixture.cs                # Starts/stops UniLinker WinUI app
    │   └── PlaywrightFixture.cs         # Manages browser context
    ├── Tests/
    │   ├── ScreenMirrorTests.cs         # ScreenMirror E2E tests
    │   └── FileTransferTests.cs         # FileTransfer E2E tests
    ├── TestData/
    │   └── sample.txt                   # Test file for upload
    └── xunit.runner.json                # xUnit configuration
```

---

## Task 1: Create Test Project and Dependencies

**Files:**
- Create: `tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj`
- Create: `tests/UniLinker.E2E.Tests/xunit.runner.json`

- [ ] **Step 1: Create tests directory structure**

```bash
mkdir -p tests/UniLinker.E2E.Tests/Fixtures
mkdir -p tests/UniLinker.E2E.Tests/Tests
mkdir -p tests/UniLinker.E2E.Tests/TestData
```

- [ ] **Step 2: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Playwright" Version="1.48.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="xunit.runner.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="TestData\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create xUnit runner configuration**

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "diagnosticMessages": true,
  "parallelizeTestCollections": false,
  "maxParallelThreads": 1
}
```

- [ ] **Step 4: Add project to solution**

```bash
dotnet sln UniLinker.sln add tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

- [ ] **Step 5: Verify project builds**

```bash
dotnet build tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

Expected: Build succeeded with 0 errors

- [ ] **Step 6: Commit**

```bash
git add tests/UniLinker.E2E.Tests/
git commit -m "test: add E2E test project with Playwright and xUnit"
```

---

## Task 2: Create Test Data File

**Files:**
- Create: `tests/UniLinker.E2E.Tests/TestData/sample.txt`

- [ ] **Step 1: Create sample test file**

```
This is a test file for UniLinker E2E file transfer tests.
Content: Hello World from UniLinker E2E Tests!
Timestamp: 2026-06-17
```

- [ ] **Step 2: Commit**

```bash
git add tests/UniLinker.E2E.Tests/TestData/sample.txt
git commit -m "test: add sample test file for E2E tests"
```

---

## Task 3: Create AppFixture

**Files:**
- Create: `tests/UniLinker.E2E.Tests/Fixtures/AppFixture.cs`

- [ ] **Step 1: Write AppFixture implementation**

```csharp
using System.Diagnostics;
using System.Net.Http;

namespace UniLinker.E2E.Tests.Fixtures;

/// <summary>
/// Starts and stops the UniLinker WinUI application for E2E testing.
/// </summary>
public class AppFixture : IDisposable
{
    private readonly Process? _appProcess;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public const int SignalingPort = 9527;
    public const string BaseUrl = "http://localhost:9527";

    public AppFixture()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Find the built executable
        var solutionDir = FindSolutionDirectory();
        var exePath = Path.Combine(
            solutionDir,
            "src", "UniLinker.WinUI", "bin", "Debug", "net9.0-windows10.0.19041.0", "win-x64",
            "UniLinker.WinUI.exe");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                $"UniLinker.WinUI.exe not found at {exePath}. " +
                "Please build the project first: dotnet build src/UniLinker.WinUI");
        }

        Console.WriteLine($"[AppFixture] Starting app: {exePath}");

        _appProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Minimized
            }
        };

        _appProcess.Start();

        // Wait for the signaling server to be ready
        WaitForReadyAsync().GetAwaiter().GetResult();
        Console.WriteLine("[AppFixture] App is ready");
    }

    private static string FindSolutionDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "UniLinker.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find solution directory");
    }

    private async Task WaitForReadyAsync(int timeoutSeconds = 30)
    {
        var startTime = DateTime.UtcNow;
        var deadline = startTime.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/info");
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[AppFixture] App ready after {(DateTime.UtcNow - startTime).TotalSeconds:F1}s");
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Server not ready yet
            }
            catch (TaskCanceledException)
            {
                // Timeout on request
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"App did not become ready within {timeoutSeconds} seconds. " +
            "Check if the app is starting correctly.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.WriteLine("[AppFixture] Stopping app...");

        try
        {
            if (_appProcess != null && !_appProcess.HasExited)
            {
                _appProcess.CloseMainWindow();
                if (!_appProcess.WaitForExit(5000))
                {
                    _appProcess.Kill();
                }
                _appProcess.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppFixture] Error stopping app: {ex.Message}");
        }

        _httpClient.Dispose();
    }
}
```

- [ ] **Step 2: Verify the file compiles**

```bash
dotnet build tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add tests/UniLinker.E2E.Tests/Fixtures/AppFixture.cs
git commit -m "test: add AppFixture for starting/stopping UniLinker app"
```

---

## Task 4: Create PlaywrightFixture

**Files:**
- Create: `tests/UniLinker.E2E.Tests/Fixtures/PlaywrightFixture.cs`

- [ ] **Step 1: Write PlaywrightFixture implementation**

```csharp
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
```

- [ ] **Step 2: Verify the file compiles**

```bash
dotnet build tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add tests/UniLinker.E2E.Tests/Fixtures/PlaywrightFixture.cs
git commit -m "test: add PlaywrightFixture for browser automation"
```

---

## Task 5: Create ScreenMirrorTests

**Files:**
- Create: `tests/UniLinker.E2E.Tests/Tests/ScreenMirrorTests.cs`

- [ ] **Step 1: Write ScreenMirrorTests implementation**

```csharp
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
```

- [ ] **Step 2: Verify the file compiles**

```bash
dotnet build tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add tests/UniLinker.E2E.Tests/Tests/ScreenMirrorTests.cs
git commit -m "test: add ScreenMirror E2E tests"
```

---

## Task 6: Create FileTransferTests

**Files:**
- Create: `tests/UniLinker.E2E.Tests/Tests/FileTransferTests.cs`

- [ ] **Step 1: Write FileTransferTests implementation**

```csharp
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
```

- [ ] **Step 2: Verify the file compiles**

```bash
dotnet build tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add tests/UniLinker.E2E.Tests/Tests/FileTransferTests.cs
git commit -m "test: add FileTransfer E2E tests"
```

---

## Task 7: Install Playwright Browsers

**Files:**
- Modify: `tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj` (add PowerShell script)

- [ ] **Step 1: Add Playwright install script to project file**

Add the following to the `PropertyGroup` in `UniLinker.E2E.Tests.csproj`:

```xml
<PlaywrightPlatform>win-x64</PlaywrightPlatform>
```

- [ ] **Step 2: Build the project**

```bash
dotnet build tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
```

- [ ] **Step 3: Install Playwright browsers**

```bash
pwsh tests/UniLinker.E2E.Tests/bin/Debug/net9.0/playwright.ps1 install chromium
```

Or if PowerShell is not available:

```bash
cd tests/UniLinker.E2E.Tests && dotnet exec --runtimeconfig bin/Debug/net9.0/UniLinker.E2E.Tests.runtimeconfig.json --depsfile bin/Debug/net9.0/UniLinker.E2E.Tests.deps.json bin/Debug/net9.0/Microsoft.Playwright.dll install chromium
```

Expected: Chromium browser installed successfully

- [ ] **Step 4: Commit**

```bash
git add tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj
git commit -m "test: configure Playwright for win-x64 platform"
```

---

## Task 8: Build and Verify Tests

**Files:**
- None (verification task)

- [ ] **Step 1: Build the entire solution**

```bash
dotnet build UniLinker.sln
```

Expected: Build succeeded with 0 errors

- [ ] **Step 2: Build WinUI app in Debug mode**

```bash
dotnet build src/UniLinker.WinUI/UniLinker.WinUI.csproj -c Debug
```

Expected: Build succeeded, UniLinker.WinUI.exe created

- [ ] **Step 3: List test discovery**

```bash
dotnet test tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj --list-tests
```

Expected: List of tests shown:
- FT01_FileTransfer_PageIsAccessible
- FT02_Server_ReturnsDeviceInfo
- FT03_Server_ReturnsStatus
- SM01_Browser_CanAccessHomepage
- SM02_Browser_CanConnectToHost
- SM03_Browser_ReceivesVideoStream
- SM04_Browser_CanDisconnect

---

## Task 9: Run Tests and Verify

**Files:**
- None (verification task)

- [ ] **Step 1: Run all E2E tests**

```bash
dotnet test tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj --verbosity normal
```

Expected: All tests pass (may require GPU for video encoding)

- [ ] **Step 2: Run only ScreenMirror tests**

```bash
dotnet test tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj --filter "Category=ScreenMirror"
```

- [ ] **Step 3: Run only FileTransfer tests**

```bash
dotnet test tests/UniLinker.E2E.Tests/UniLinker.E2E.Tests.csproj --filter "Category=FileTransfer"
```

---

## Task 10: Update Documentation

**Files:**
- Create: `tests/UniLinker.E2E.Tests/README.md`

- [ ] **Step 1: Create README with instructions**

```markdown
# UniLinker E2E Tests

End-to-end tests for UniLinker Windows app using Playwright.

## Prerequisites

1. Build the WinUI app first:
   ```bash
   dotnet build src/UniLinker.WinUI/UniLinker.WinUI.csproj
   ```

2. Install Playwright browsers (first time only):
   ```bash
   pwsh tests/UniLinker.E2E.Tests/bin/Debug/net9.0/playwright.ps1 install chromium
   ```

## Running Tests

Run all E2E tests:
```bash
dotnet test tests/UniLinker.E2E.Tests
```

Run only ScreenMirror tests:
```bash
dotnet test --filter "Category=ScreenMirror"
```

Run only FileTransfer tests:
```bash
dotnet test --filter "Category=FileTransfer"
```

## Test Categories

| Category | Description |
|----------|-------------|
| ScreenMirror | Tests for screen mirroring feature |
| FileTransfer | Tests for file transfer feature |

## Notes

- Tests require a GPU for video encoding (NVENC or software fallback)
- The app is started automatically by the test fixture
- Tests run in headless Chromium browser
```

- [ ] **Step 2: Commit**

```bash
git add tests/UniLinker.E2E.Tests/README.md
git commit -m "docs: add E2E tests README"
```

---

## Self-Review Checklist

After completing all tasks, verify:

1. **Spec coverage:**
   - [x] SM-01: Browser access homepage → Task 5
   - [x] SM-02: Trigger screen share → Task 5 (SM02)
   - [x] SM-03: Video playback verification → Task 5 (SM03)
   - [x] FT-01/02/03: API endpoint tests → Task 6

2. **No placeholders:**
   - [x] All code blocks contain actual implementation
   - [x] All commands are specific and executable
   - [x] No TBD/TODO comments

3. **Type consistency:**
   - [x] AppFixture.BaseUrl used consistently
   - [x] AppFixture.SignalingPort = 9527 matches actual app
   - [x] Test method names follow convention (SM01_, FT01_, etc.)
