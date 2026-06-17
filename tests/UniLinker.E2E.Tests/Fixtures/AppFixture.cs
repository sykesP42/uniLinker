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