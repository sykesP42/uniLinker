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
   
   Or using Node.js:
   ```bash
   cd tests/UniLinker.E2E.Tests/bin/Debug/net9.0 && node cli.js install chromium
   ```

## Running Tests

Run all E2E tests:
```bash
dotnet test tests/UniLinker.E2E.Tests
```

Run only ScreenMirror tests:
```bash
dotnet test tests/UniLinker.E2E.Tests --filter "Category=ScreenMirror"
```

Run only FileTransfer tests:
```bash
dotnet test tests/UniLinker.E2E.Tests --filter "Category=FileTransfer"
```

## Test Categories

| Category | Description | Tests |
|----------|-------------|-------|
| ScreenMirror | Tests for screen mirroring feature | SM01-SM04 |
| FileTransfer | Tests for file transfer API endpoints | FT01-FT03 |

## Test Details

### ScreenMirror Tests (4 tests)

| Test | Description |
|------|-------------|
| SM01_Browser_CanAccessHomepage | Verifies homepage loads with logo and connect button |
| SM02_Browser_CanConnectToHost | Tests WebRTC SDP exchange and connection |
| SM03_Browser_ReceivesVideoStream | Verifies video stream is received and playing |
| SM04_Browser_CanDisconnect | Tests disconnect functionality |

### FileTransfer Tests (3 tests)

| Test | Description |
|------|-------------|
| FT01_FileTransfer_PageIsAccessible | Verifies /info endpoint is accessible |
| FT02_Server_ReturnsDeviceInfo | Verifies device info response |
| FT03_Server_ReturnsStatus | Verifies /api/status endpoint |

## Notes

- Tests require the UniLinker WinUI app to be built and available
- The app is started automatically by the test fixture (AppFixture)
- Tests run in headless Chromium browser
- Video tests may require GPU for encoding (NVENC or software fallback)
- Tests use xUnit with sequential execution (no parallelization)

## Troubleshooting

### Playwright browser not installed

Run the install command again:
```bash
pwsh tests/UniLinker.E2E.Tests/bin/Debug/net9.0/playwright.ps1 install chromium
```

### App not starting

Ensure UniLinker.WinUI.exe exists at:
```
src/UniLinker.WinUI/bin/Debug/net9.0-windows10.0.19041.0/win-x64/UniLinker.WinUI.exe
```

### Video tests failing

Video tests require the screen capture plugin to work. Ensure:
- ScreenMirror plugin is registered in Platform
- SignalingServer is running on port 9527
- WebRTC connection can be established