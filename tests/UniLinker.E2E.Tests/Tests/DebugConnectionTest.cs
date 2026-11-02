using FluentAssertions;
using Microsoft.Playwright;
using UniLinker.E2E.Tests.Fixtures;
using Xunit;

namespace UniLinker.E2E.Tests.Tests;

/// <summary>
/// Temporary diagnostic test: connects and dumps browser console output,
/// WebRTC connection states, and video element stats.
/// </summary>
public class DebugConnectionTest : IClassFixture<AppFixture>, IAsyncLifetime
{
    private readonly AppFixture _appFixture;
    private readonly PlaywrightFixture _playwrightFixture;
    private IPage? _page;

    public DebugConnectionTest(AppFixture appFixture)
    {
        _appFixture = appFixture;
        _playwrightFixture = new PlaywrightFixture();
    }

    public async Task InitializeAsync()
    {
        _page = await _playwrightFixture.NewPageAsync();
        _page.Console += (_, msg) => Console.WriteLine($"[console:{msg.Type}] {msg.Text}");
        _page.PageError += (_, err) => Console.WriteLine($"[pageerror] {err}");
        _page.RequestFailed += (_, req) => Console.WriteLine($"[reqfail] {req.Url} {req.Failure}");
    }

    public async Task DisposeAsync()
    {
        await _playwrightFixture.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Debug")]
    public async Task Debug_ConnectAndDump()
    {
        await _page!.GotoAsync($"{AppFixture.BaseUrl}/index.html");

        // Do the SDP exchange manually and dump the raw answer body.
        var result = await _page.EvaluateAsync<string>(@"async () => {
            const pc = new RTCPeerConnection({ iceServers: [] });
            pc.addTransceiver('video', { direction: 'recvonly' });
            const offer = await pc.createOffer();
            await pc.setLocalDescription(offer);
            await new Promise(r => {
                if (pc.iceGatheringState === 'complete') return r();
                pc.onicegatheringstatechange = () => {
                    if (pc.iceGatheringState === 'complete') r();
                };
                setTimeout(r, 3000);
            });
            const resp = await fetch('/signaling', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    type: 'offer',
                    sdp: pc.localDescription.sdp,
                    fromPeerId: 'debugtest',
                    capability: 'screen-capture'
                })
            });
            const text = await resp.text();
            return JSON.stringify({ status: resp.status, body: text, offer: pc.localDescription.sdp });
        }");
        Console.WriteLine($"[answer raw] {result}");
        var obj = System.Text.Json.JsonDocument.Parse(result).RootElement;
        if (obj.TryGetProperty("offer", out var offerEl))
        {
            var sdp = offerEl.GetString();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "offer.sdp"), sdp ?? "");
            Console.WriteLine("[offer saved] length=" + (sdp?.Length ?? 0));
        }
    }

    [Fact]
    [Trait("Category", "Debug")]
    public async Task Debug_ConnectAndScreenshot()
    {
        await _page!.GotoAsync($"{AppFixture.BaseUrl}/index.html");
        await _page.Locator("#hostInput").FillAsync("localhost");
        await _page.Locator("#btnConnect").ClickAsync();
        await _page.WaitForFunctionAsync(
            @"() => {
                const video = document.getElementById('remoteVideo');
                return video && video.readyState >= 2 && video.videoWidth > 0;
            }",
            new PageWaitForFunctionOptions { Timeout = 30000 });
        await _page.EvaluateAsync(@"async () => {
            const video = document.getElementById('remoteVideo');
            await video.play().catch(() => {});
        }");
        await Task.Delay(2000);
        await _page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(Path.GetTempPath(), "viewer-live.png")
        });
        Console.WriteLine("[screenshot saved]");
    }
}
