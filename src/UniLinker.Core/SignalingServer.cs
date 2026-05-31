using System.Net;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// HTTP server for WebRTC SDP + ICE candidate signaling exchange.
/// Each uniLinker instance runs one.
///
/// Endpoints:
///   GET  /info        — device metadata and capabilities
///   POST /signaling   — SDP exchange (offer/answer) and ICE candidate relay
///   POST /ice         — ICE candidate from browser/peer
/// </summary>
public class SignalingServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly PeerMesh _peerMesh;
    private readonly string[] _capabilities;
    private readonly string _webRoot;
    private readonly Dictionary<string, PeerConnection> _pendingIceConnections = new();
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
        _webRoot = Path.Combine(AppContext.BaseDirectory, "web");
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
            // Access denied -- try with localhost only
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
            // CORS headers on every response
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

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

                case "/ice" when ctx.Request.HttpMethod == "POST":
                    await HandleIceCandidate(ctx);
                    break;

                default:
                    // Serve static files from web/ directory
                    if (ctx.Request.HttpMethod == "GET")
                        await ServeStaticFile(ctx, path);
                    else
                    {
                        ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                    }
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
            await HandleOffer(ctx, msg);
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

    private async Task HandleOffer(HttpListenerContext ctx, SignalingMessage msg)
    {
        OfferReceived?.Invoke(msg.Sdp, msg.FromPeerId);

        var peerInfo = new PeerInfo
        {
            Id = msg.FromPeerId,
            Name = msg.FromPeerId,
            IpAddress = ctx.Request.RemoteEndPoint?.Address?.ToString() ?? "127.0.0.1",
            Port = _port,
            State = PeerState.Discovered,
            Capabilities = msg.Capability != null ? [msg.Capability] : [],
        };

        // Create a new PeerConnection, initialize it, and set the remote offer
        var pc = new PeerConnection(peerInfo);
        var ok = await pc.InitializeAsync();
        if (!ok)
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
            return;
        }

        await pc.SetRemoteDescriptionAsync(msg.Sdp, isOffer: true);

        // Register in PeerMesh so plugins can send to it
        _peerMesh.RegisterIncomingConnection(peerInfo);

        // Store for ICE candidate forwarding
        lock (_pendingIceConnections)
        {
            _pendingIceConnections[msg.FromPeerId] = pc;
        }

        // Fire ChannelRequested on PeerMesh so plugins can respond
        if (!string.IsNullOrEmpty(msg.Capability))
        {
            _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, msg.Capability);
        }
        else
        {
            foreach (var cap in _capabilities)
            {
                _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, cap);
            }
        }

        var localSdp = pc.GetLocalSdp();
        System.Diagnostics.Debug.WriteLine(
            $"[Signaling] Offer from {msg.FromPeerId}, answer ready");

        var answer = new SignalingMessage
        {
            Type = "answer",
            Sdp = localSdp ?? "",
            FromPeerId = Environment.MachineName,
        };

        await SendJson(ctx, answer);
    }

    private async Task HandleIceCandidate(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        var msg = JsonSerializer.Deserialize<IceCandidateMessage>(body);
        if (msg == null || string.IsNullOrEmpty(msg.PeerId))
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        lock (_pendingIceConnections)
        {
            if (_pendingIceConnections.TryGetValue(msg.PeerId, out var pc))
            {
                pc.AddIceCandidate(new RTCIceCandidateInit
                {
                    candidate = msg.Candidate ?? "",
                    sdpMid = msg.SdpMid ?? "0",
                    sdpMLineIndex = (ushort)msg.SdpMLineIndex,
                });
                System.Diagnostics.Debug.WriteLine(
                    $"[Signaling] ICE candidate from {msg.PeerId}");
            }
        }

        ctx.Response.StatusCode = 200;
        ctx.Response.Close();
    }

    private async Task ServeStaticFile(HttpListenerContext ctx, string path)
    {
        // Map URL path to file path
        // "/" or "/index.html" -> index.html
        // "/app.js" -> app.js
        // "/styles.css" -> styles.css

        var filePath = path == "/" || path == "/index.html"
            ? Path.Combine(_webRoot, "index.html")
            : Path.Combine(_webRoot, path.TrimStart('/'));

        // Security: ensure file is within web root
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(Path.GetFullPath(_webRoot), StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }

        if (!File.Exists(fullPath))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        // Set content type based on extension
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };

        var content = await File.ReadAllBytesAsync(fullPath);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = content.Length;
        await ctx.Response.OutputStream.WriteAsync(content);
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
        lock (_pendingIceConnections)
        {
            foreach (var pc in _pendingIceConnections.Values)
                pc.Dispose();
            _pendingIceConnections.Clear();
        }
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
}

public class SignalingMessage
{
    public string Type { get; set; } = ""; // "offer" or "answer"
    public string Sdp { get; set; } = "";
    public string FromPeerId { get; set; } = "";
    public string? Capability { get; set; }
}

public class IceCandidateMessage
{
    public string PeerId { get; set; } = "";
    public string? Candidate { get; set; }
    public string? SdpMid { get; set; }
    public int SdpMLineIndex { get; set; }
}
