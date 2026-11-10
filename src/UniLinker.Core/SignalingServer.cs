using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// HTTP server for WebRTC SDP + ICE candidate signaling exchange.
/// Every UniLinker instance runs one, reachable from the LAN.
///
/// Implemented directly on TcpListener instead of HttpListener so that
/// binding to all interfaces does not require an http.sys URL ACL
/// (admin rights). Endpoints:
///   GET  /info         — device metadata and capabilities
///   GET  /api/status   — runtime status
///   POST /signaling    — SDP exchange (offer/answer)
///   POST /ice          — ICE candidate from browser/peer
///   GET  /ice-pending  — ICE candidates waiting for a peer
///   GET  /*            — static viewer page from the web root
/// </summary>
public class SignalingServer : IDisposable
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private readonly int _port;
    private readonly PeerMesh _peerMesh;
    private readonly string[] _capabilities;
    private readonly string _webRoot;
    private readonly Dictionary<string, PeerConnection> _pendingIceConnections = new();
    private readonly Dictionary<string, List<IceCandidateMessage>> _pendingOutgoingIce = new();
    private readonly object _iceLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }

    public SignalingServer(
        int port,
        PeerMesh peerMesh,
        string[]? capabilities = null)
    {
        _port = port;
        _peerMesh = peerMesh;
        _capabilities = capabilities ?? ["screen-capture", "h264-encode", "video-stream"];
        _webRoot = Path.Combine(AppContext.BaseDirectory, "web");
    }

    public int Port => _port;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _listener = new TcpListener(IPAddress.IPv6Any, _port);
        _listener.Server.DualMode = true; // covers IPv4-mapped addresses too
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        IsRunning = true;
        Log.Info($"Signaling server listening on port {_port} (all interfaces)");

        _ = AcceptLoopAsync(token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var listener = _listener!;
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalingServer] accept error: {ex.Message}");
                continue;
            }
            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            using var _ = client;
            client.NoDelay = true;
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 15000;

            var stream = client.GetStream();
            var request = await ReadHttpRequestAsync(stream);
            if (request == null)
            {
                await WriteResponseAsync(stream, 400, "text/plain", "Bad request"u8.ToArray());
                return;
            }

            await RouteAsync(stream, request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignalingServer] client error: {ex.Message}");
        }
    }

    private async Task RouteAsync(NetworkStream stream, HttpRequest request)
    {
        if (request.Method == "OPTIONS")
        {
            await WriteResponseAsync(stream, 204, "text/plain", [], withCors: true);
            return;
        }

        var path = request.Path;

        switch (path)
        {
            case "/info" when request.Method == "GET":
                await WriteJsonAsync(stream, 200, new
                {
                    name = GetDeviceName(),
                    version = UniLinkerInfo.Version,
                    capabilities = _capabilities,
                    status = "available",
                });
                break;

            case "/api/status" when request.Method == "GET":
                await WriteJsonAsync(stream, 200, new
                {
                    deviceName = GetDeviceName(),
                    version = UniLinkerInfo.Version,
                    port = _port,
                    peers = _peerMesh.ConnectedPeers.Count,
                    status = IsRunning ? "running" : "stopped",
                });
                break;

            case "/signaling" when request.Method == "POST":
                await HandleSignalingAsync(stream, request);
                break;

            case "/ice" when request.Method == "POST":
                await HandleIceCandidateAsync(request);
                await WriteJsonAsync(stream, 200, new { ok = true });
                break;

            case "/ice-pending" when request.Method == "GET":
                await HandleIcePendingAsync(stream, request.Query);
                break;

            default:
                if (request.Method == "GET")
                    await ServeStaticFileAsync(stream, path);
                else
                    await WriteResponseAsync(stream, 404, "text/plain", "Not found"u8.ToArray(), withCors: true);
                break;
        }
    }

    private async Task HandleSignalingAsync(NetworkStream stream, HttpRequest request)
    {
        SignalingMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<SignalingMessage>(request.Body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
        catch (JsonException)
        {
            msg = null;
        }
        if (msg == null || string.IsNullOrEmpty(msg.Sdp))
        {
            await WriteResponseAsync(stream, 400, "text/plain", "Invalid signaling message"u8.ToArray(), withCors: true);
            return;
        }

        if (msg.Type == "offer")
        {
            await HandleOfferAsync(stream, msg);
        }
        else if (msg.Type == "answer")
        {
            // Answer to an outgoing connection we initiated (not used by the
            // browser viewer flow, which polls /ice-pending instead).
            await WriteJsonAsync(stream, 200, new { ok = true });
        }
        else
        {
            await WriteResponseAsync(stream, 400, "text/plain", "Unknown message type"u8.ToArray(), withCors: true);
        }
    }

    private async Task HandleOfferAsync(NetworkStream stream, SignalingMessage msg)
    {
        var peerId = string.IsNullOrEmpty(msg.FromPeerId) ? "unknown" : msg.FromPeerId;
        var peerInfo = new PeerInfo
        {
            Id = peerId,
            Name = peerId,
            IpAddress = "0.0.0.0", // filled in by the server from the socket
            Port = _port,
            State = PeerState.Discovered,
            Capabilities = msg.Capability != null ? [msg.Capability] : [],
        };

        var pc = new PeerConnection(peerInfo);
        if (!await pc.InitializeAsync())
        {
            await WriteResponseAsync(stream, 500, "text/plain", "WebRTC init failed"u8.ToArray(), withCors: true);
            return;
        }

        pc.SetRemoteDescriptionAsync(msg.Sdp, isOffer: true);

        // Register the SAME connection in the mesh so plugin channels are
        // bound to the negotiated peer connection, not a fresh dead one.
        _peerMesh.RegisterIncomingConnection(peerInfo, pc);

        lock (_pendingIceConnections)
        {
            _pendingIceConnections[peerId] = pc;
        }

        lock (_iceLock)
        {
            _pendingOutgoingIce[peerId] = new List<IceCandidateMessage>();
        }
        void OnCandidate(List<SIPSorcery.Net.RTCIceCandidate> candidates)
        {
            lock (_iceLock)
            {
                if (_pendingOutgoingIce.TryGetValue(peerId, out var list))
                {
                    foreach (var c in candidates)
                    {
                        list.Add(new IceCandidateMessage
                        {
                            PeerId = peerId,
                            Candidate = c.candidate ?? "",
                            SdpMid = c.sdpMid ?? "0",
                            SdpMLineIndex = c.sdpMLineIndex,
                        });
                    }
                }
            }
        }
        pc.IceCandidatesGenerated += OnCandidate;
        pc.StateChanged += state =>
        {
            if (state is PeerConnectionState.Closed or PeerConnectionState.Failed)
            {
                pc.IceCandidatesGenerated -= OnCandidate;
                lock (_pendingIceConnections)
                {
                    _pendingIceConnections.Remove(peerId);
                }
            }
        };

        // Wait for ICE gathering so the answer SDP carries our candidates.
        await pc.WaitForIceGatheringAsync(TimeSpan.FromSeconds(3));

        // Fire the channel request so the owning plugin starts producing.
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

        var localSdp = pc.GetLocalSdp() ?? "";
        Log.Info($"Offer handled for '{peerId}' capability={msg.Capability} answerSdpBytes={localSdp.Length}");

        // Remember the peer's real address for the UI (available after the
        // socket round trip; the request handler passes it via Body marker).
        var answer = new SignalingMessage
        {
            Type = "answer",
            Sdp = localSdp,
            FromPeerId = GetDeviceName(),
        };

        await WriteJsonAsync(stream, 200, answer, withCors: true);
    }

    private async Task HandleIceCandidateAsync(HttpRequest request)
    {
        IceCandidateMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<IceCandidateMessage>(request.Body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
        catch (JsonException)
        {
            msg = null;
        }

        if (msg == null || string.IsNullOrEmpty(msg.PeerId)) return;

        lock (_pendingIceConnections)
        {
            if (_pendingIceConnections.TryGetValue(msg.PeerId, out var pc))
            {
                pc.AddIceCandidate(new SIPSorcery.Net.RTCIceCandidateInit
                {
                    candidate = msg.Candidate ?? "",
                    sdpMid = msg.SdpMid ?? "0",
                    sdpMLineIndex = (ushort)msg.SdpMLineIndex,
                });
            }
        }
        await Task.CompletedTask;
    }

    private async Task HandleIcePendingAsync(NetworkStream stream, string query)
    {
        var peerId = GetQueryValue(query, "peerId");
        if (string.IsNullOrEmpty(peerId))
        {
            await WriteResponseAsync(stream, 400, "text/plain", "peerId required"u8.ToArray(), withCors: true);
            return;
        }

        List<IceCandidateMessage>? candidates;
        lock (_iceLock)
        {
            if (_pendingOutgoingIce.TryGetValue(peerId, out var list))
            {
                candidates = new List<IceCandidateMessage>(list);
                list.Clear();
            }
            else
            {
                candidates = null;
            }
        }

        if (candidates == null)
        {
            await WriteJsonAsync(stream, 404, new { candidates = Array.Empty<IceCandidateMessage>() }, withCors: true);
            return;
        }

        await WriteJsonAsync(stream, 200, new { candidates }, withCors: true);
    }

    private async Task ServeStaticFileAsync(NetworkStream stream, string path)
    {
        var relative = path == "/" || path == "/index.html" ? "index.html" : path.TrimStart('/');
        var filePath = Path.GetFullPath(Path.Combine(_webRoot, relative));

        if (!filePath.StartsWith(Path.GetFullPath(_webRoot), StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 403, "text/plain", "Forbidden"u8.ToArray());
            return;
        }

        if (!File.Exists(filePath))
        {
            await WriteResponseAsync(stream, 404, "text/plain", "Not found"u8.ToArray());
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };

        var content = await File.ReadAllBytesAsync(filePath);
        await WriteResponseAsync(stream, 200, contentType, content);
    }

    // ── HTTP plumbing ──

    private static async Task<HttpRequest?> ReadHttpRequestAsync(NetworkStream stream)
    {
        var headerBytes = new MemoryStream();
        var buffer = new byte[4096];
        int headerEnd = -1;
        int total = 0;

        while (total < MaxHeaderBytes)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read == 0) return null;
            headerBytes.Write(buffer, 0, read);
            total += read;

            var bytes = headerBytes.GetBuffer();
            for (int i = 0; i < total - 3; i++)
            {
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                {
                    headerEnd = i + 4;
                    break;
                }
            }
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return null;

        var headerText = Encoding.UTF8.GetString(headerBytes.GetBuffer(), 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var clValue))
        {
            int.TryParse(clValue, out contentLength);
        }
        if (contentLength < 0 || contentLength > MaxBodyBytes) return null;

        var body = new byte[contentLength];
        if (contentLength > 0)
        {
            int remaining = contentLength;
            int offset = 0;
            int available = total - headerEnd;
            if (available > 0)
            {
                int take = Math.Min(available, contentLength);
                Buffer.BlockCopy(headerBytes.GetBuffer(), headerEnd, body, 0, take);
                offset = take;
                remaining -= take;
            }
            while (remaining > 0)
            {
                int read = await stream.ReadAsync(body.AsMemory(offset, remaining));
                if (read == 0) return null;
                offset += read;
                remaining -= read;
            }
        }

        var pathAndQuery = requestLine[1];
        int qmark = pathAndQuery.IndexOf('?');
        var path = qmark >= 0 ? pathAndQuery[..qmark] : pathAndQuery;
        var query = qmark >= 0 ? pathAndQuery[(qmark + 1)..] : "";

        return new HttpRequest(requestLine[0].ToUpperInvariant(), path, query, headers, body);
    }

    private static string GetQueryValue(string query, string key)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0) continue;
            if (pair[..eq].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
        return "";
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, int statusCode, string contentType, byte[] content, bool withCors = false)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ')
          .Append(StatusText(statusCode)).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(content.Length).Append("\r\n");
        if (withCors)
        {
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
        }
        sb.Append("Connection: close\r\n\r\n");

        var head = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(head);
        if (content.Length > 0)
        {
            await stream.WriteAsync(content);
        }
        await stream.FlushAsync();
    }

    private static Task WriteJsonAsync(NetworkStream stream, int statusCode, object data, bool withCors = false)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        return WriteResponseAsync(stream, statusCode, "application/json", Encoding.UTF8.GetBytes(json), withCors);
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "OK",
    };

    private static string GetDeviceName()
    {
        try { return Environment.MachineName; }
        catch { return "unknown"; }
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        lock (_pendingIceConnections)
        {
            foreach (var pc in _pendingIceConnections.Values)
                pc.Dispose();
            _pendingIceConnections.Clear();
        }
        lock (_iceLock)
        {
            _pendingOutgoingIce.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
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

internal record HttpRequest(
    string Method,
    string Path,
    string Query,
    Dictionary<string, string> Headers,
    byte[] Body);
