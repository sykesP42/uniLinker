using System.Net;
using System.Text;
using System.Text.Json;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Minimal HTTP server for WebRTC SDP signaling exchange.
/// Each uniLinker instance runs one.
/// POST /signaling — exchange SDP offer/answer
/// GET /info — device metadata and capabilities
/// </summary>
public class SignalingServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly PeerMesh _peerMesh;
    private readonly string[] _capabilities;
    private CancellationTokenSource? _cts;

    public event Action<string, string>? OfferReceived; // sdp, fromPeerId
    public event Action<string>? SdpAnswerReceived;

    public SignalingServer(
        int port,
        PeerMesh peerMesh,
        string[]? capabilities = null)
    {
        _port = port;
        _peerMesh = peerMesh;
        _capabilities = capabilities ?? ["screen-capture", "h264-encode", "video-stream"];
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            // Access denied — try with localhost only
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var ctxTask = _listener.GetContextAsync();
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var completedTask = await Task.WhenAny(
                    ctxTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));

                if (completedTask != ctxTask || _cts.IsCancellationRequested)
                    break;

                var ctx = await ctxTask;
                _ = HandleRequestAsync(ctx);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { /* shutting down */ }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case "/info":
                    await SendJson(ctx, new
                    {
                        name = Environment.MachineName,
                        version = "3.0.0",
                        capabilities = _capabilities,
                        status = "available",
                    });
                    break;

                case "/signaling" when ctx.Request.HttpMethod == "POST":
                    await HandleSignaling(ctx);
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Signaling error: {ex.Message}");
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch { /* best effort */ }
        }
    }

    private async Task HandleSignaling(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        var msg = JsonSerializer.Deserialize<SignalingMessage>(body);
        if (msg == null)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        if (msg.Type == "offer")
        {
            OfferReceived?.Invoke(msg.Sdp, msg.FromPeerId);

            // Parse the offer to extract remote IP and RTP port
            var (remoteIp, remotePort) = ParseSdpEndpoint(msg.Sdp);

            var peerInfo = new PeerInfo
            {
                Id = msg.FromPeerId,
                Name = msg.FromPeerId,
                IpAddress = remoteIp ?? "",
                State = PeerState.Discovered,
                Capabilities = msg.Capability != null ? [msg.Capability] : [],
            };

            // Register the connection in PeerMesh with the remote endpoint set.
            // This PeerConnection will be reused when ChannelRequested fires and
            // the plugin calls CreateChannel.
            PeerConnection registeredPc;
            if (remoteIp != null && remotePort.HasValue)
            {
                registeredPc = _peerMesh.RegisterIncomingConnection(
                    peerInfo, remoteIp, remotePort.Value);
            }
            else
            {
                // No endpoint in offer — create a PeerConnection just for the answer
                registeredPc = _peerMesh.RegisterIncomingConnection(
                    peerInfo, peerInfo.IpAddress, 0);
            }

            // Generate answer from the registered PeerConnection (has correct local port)
            var realSdp = registeredPc.CreateAnswer();

            var answer = new SignalingMessage
            {
                Type = "answer",
                Sdp = realSdp,
                FromPeerId = Environment.MachineName,
            };

            // Fire ChannelRequested on PeerMesh so plugins can respond
            if (!string.IsNullOrEmpty(msg.Capability))
            {
                _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, msg.Capability);
            }
            else
            {
                // Try with default capabilities
                foreach (var cap in _capabilities)
                {
                    _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, cap);
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Signaling] Offer from {msg.FromPeerId}, remote={remoteIp}:{remotePort}, " +
                $"answer localPort={registeredPc.LocalRtpPort}");

            await SendJson(ctx, answer);
        }
        else if (msg.Type == "answer")
        {
            SdpAnswerReceived?.Invoke(msg.Sdp);
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        }
        else
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
        }
    }

    /// <summary>Extract connection IP and RTP port from an SDP message.</summary>
    private static (string? ip, int? port) ParseSdpEndpoint(string sdp)
    {
        string? ip = null;
        int? port = null;

        foreach (var line in sdp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("c=IN IP4 "))
                ip = trimmed["c=IN IP4 ".Length..];
            else if (trimmed.StartsWith("m=video "))
            {
                var parts = trimmed.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[1], out var p))
                    port = p;
            }
        }

        return (ip, port);
    }

    private static async Task SendJson(HttpListenerContext ctx, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var buf = Encoding.UTF8.GetBytes(json);

        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        try { (_listener as IDisposable)?.Dispose(); } catch { }
    }
}

public class SignalingMessage
{
    public string Type { get; set; } = ""; // "offer" or "answer"
    public string Sdp { get; set; } = "";
    public string FromPeerId { get; set; } = "";
    public string? Capability { get; set; }
}
