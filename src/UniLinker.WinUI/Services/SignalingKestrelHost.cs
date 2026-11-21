using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.WinUI.Services;

/// <summary>
/// Kestrel-hosted signaling server — the M2 replacement for the hand-rolled
/// TcpListener HTTP/1.1 server (docs/spec/M2-kestrel.md). Pure transport
/// mapping: business logic lives in SignalingService (Core); JSON wire
/// shapes are locked by SignalingContractTests and the E2E suite.
///
/// Contract parity notes:
///   - "*:{port}" binds IPv6Any dual-mode, like the legacy TcpListener.
///   - Every response carries Access-Control-Allow-Origin: * (legacy CORS).
///   - OPTIONS anywhere → 204 (legacy preflight behavior).
///   - /ice-pending is intentionally NOT carried over (dead route; no
///     client ever called it), nor is the outgoing-candidate buffering.
/// </summary>
public sealed class SignalingKestrelHost : IDisposable
{
    private readonly SignalingService _service;
    private readonly int _port;
    private readonly X509Certificate2? _identity;
    private readonly int _pairingPort;
    private WebApplication? _app;

    public int Port => _port;
    public int PairingPort { get; private set; }
    public string PairingCode { get; private set; } = "";
    public bool IsRunning => _app is not null;

    public SignalingKestrelHost(
        int port,
        PeerMesh peerMesh,
        X509Certificate2? identity = null,
        int pairingPort = 9528)
    {
        _port = port;
        _identity = identity;
        _pairingPort = identity is null ? 0 : pairingPort;
        PairingPort = _pairingPort;
        _service = new SignalingService(peerMesh, port);
        PairingCode = identity is null ? "" : DeviceIdentity.DerivePairingCode(
            DeviceIdentity.ComputeFingerprint(identity));
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_app is not null) return Task.CompletedTask;

        // The viewer page lives in <exe>/web, not wwwroot. WebRoot must be
        // set via WebApplicationOptions — mutating it through
        // builder.WebHost afterwards throws NotSupportedException.
        var webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = Directory.Exists(webRoot) ? webRoot : null,
        });
        builder.Logging.ClearProviders();
        // Forward ASP.NET/Kestrel warnings+errors to the file log — silent
        // 500s were undiagnosable with the providers cleared.
        builder.Logging.AddProvider(new KestrelFileLoggerProvider());
        if (_identity is not null)
        {
            // Dual listeners: plain HTTP (browser viewer compatibility) +
            // HTTPS with the DEVICE certificate (pairing channel for apps —
            // the fingerprint of this certificate is the device identity).
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.ListenAnyIP(_port);
                kestrel.ListenAnyIP(_pairingPort, listen =>
                    listen.UseHttps(_identity!));
            });
        }
        else
        {
            builder.WebHost.UseUrls($"http://*:{_port}");
        }

        var app = builder.Build();

        Log.Info($"SignalingKestrelHost: identity={_identity != null} pairingPort={_pairingPort} urls=[{string.Join(",", app.Urls)}]");

        // Legacy parity: CORS headers on every response, preflight 204.
        // OPTIONS is short-circuited in middleware (NOT as a catch-all route:
        // a route candidate on {*path} makes ASP.NET answer 405 for GETs).
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                ctx.Response.Headers.AccessControlAllowOrigin = "*";
                return Task.CompletedTask;
            });
            if (HttpMethods.IsOptions(ctx.Request.Method))
            {
                ctx.Response.StatusCode = 204;
                return;
            }
            await next();
        });

        if (Directory.Exists(webRoot))
        {
            app.UseDefaultFiles(); // "/" → index.html (legacy root behavior)
            app.UseStaticFiles();
        }

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // M4 pairing discovery (informational only — the SAS code is compared
        // on-screen by the human; trusting this endpoint alone would defeat
        // the purpose of the pairing).
        app.MapGet("/ulp/pairing", () =>
        {
            try
            {
                var fp = _identity?.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                Log.Info($"pairing endpoint: fp={fp} code={PairingCode}");
                return Results.Json(new { pairingCode = PairingCode, fingerprint = fp }, jsonOpts);
            }
            catch (Exception ex)
            {
                Log.Error($"pairing endpoint failed: {ex}");
                return Results.Text("pairing error: " + ex.Message, "text/plain", statusCode: 500);
            }
        });

        // M5 transport selection from the receiver's floating panel:
        // POST {mode} applies immediately (new sessions) and persists to
        // settings.json (TransportIndex) so a PC restart keeps the choice.
        app.MapPost("/ulp/transport", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var json = System.Text.Json.JsonDocument.Parse(body);
                var mode = json.RootElement.GetProperty("mode").GetString() ?? "";
                if (mode != "auto" && mode != "sipsorcery" && mode != "rtx" && mode != "tile" && mode != "tile-hq")
                {
                    return Results.Json(new { applied = "", error = "unknown mode" }, jsonOpts);
                }
                // tile-hq is a Tile variant (quality 92 ladder), not a mode.
                DirectRtpTransport.Mode = mode == "tile-hq" ? "tile" : mode;
                DirectRtpTransport.TileQualityBase = mode == "tile-hq" ? 92 : 82;

                // Persist: settings.json read-modify-write (TransportIndex:
                // 0 = rtx, 1 = sipsorcery).
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "uniLinker", "settings.json");
                int transportIndex = mode switch { "auto" => 0, "sipsorcery" => 1, "rtx" => 2, "tile" => 3, _ => 3 };
                try
                {
                    var raw = File.Exists(settingsPath)
                        ? File.ReadAllText(settingsPath) : "{}";
                    var doc = System.Text.Json.JsonDocument.Parse(raw);
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.Clone();
                    }
                    dict["TransportIndex"] = transportIndex;
                    var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(settingsPath,
                        System.Text.Json.JsonSerializer.Serialize(dict, opts));
                }
                catch (Exception pex)
                {
                    Log.Info($"[transport] persist failed (mode still applied): {pex.Message}");
                }

                Log.Info($"[transport] mode set to {mode} by receiver");
                return Results.Json(new { applied = mode }, jsonOpts);
            }
            catch (Exception ex)
            {
                Log.Error($"[transport] set failed: {ex}");
                return Results.Text("transport set failed: " + ex.Message, "text/plain", statusCode: 500);
            }
        });

        app.MapGet("/ulp/transport", () =>
            Results.Json(new { mode = DirectRtpTransport.Mode }, jsonOpts));

        // M4 PLI: media-plane key-frame request from a loss-detecting receiver.
        // Body is informational; the requester's IP is logged for diagnostics.
        app.MapPost("/ulp/pli", async (HttpContext ctx) =>
        {
            string body = "";
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                body = await reader.ReadToEndAsync();
            }
            catch
            {
                // An unreadable body still counts as a request.
            }
            var remote = ctx.Connection.RemoteIpAddress?.MapToIPv4()?.ToString() ?? "unknown";
            ViewerLiveness.Notify(); // a loss-detecting receiver is demonstrably alive
            var honored = KeyFrameBroker.Request(remote);
            return Results.Json(new { honored }, jsonOpts);
        });

        // Viewer liveness heartbeat: the Android receiver POSTs this every 2 s
        // while streaming. The screen-mirror plugin's watchdog stops the
        // session when beats stop (killed app / dead network) — stale
        // sessions used to encode media into the void for hours.
        app.MapPost("/ulp/keepalive", () =>
        {
            ViewerLiveness.Notify();
            return Results.Json(new { ok = true }, jsonOpts);
        });

        app.MapGet("/info", () => Results.Json(_service.GetInfo(), jsonOpts));
        app.MapGet("/api/status", () => Results.Json(_service.GetStatus(IsRunning), jsonOpts));

        // Manual body parsing preserves the legacy 400 texts byte-for-byte.
        app.MapPost("/signaling", async (HttpContext ctx) =>
        {
            SignalingMessage? msg;
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                msg = JsonSerializer.Deserialize<SignalingMessage>(body, jsonOpts);
            }
            catch (JsonException)
            {
                msg = null;
            }

            if (msg is null)
            {
                return Results.Text("Invalid signaling message", "text/plain", statusCode: 400);
            }

            // M3 direct RTP: no SDP involved, the requester names its UDP port.
            if (msg.Type == "rtp")
            {
                if (msg.RtpPort <= 0)
                {
                    return Results.Text("rtpPort required", "text/plain", statusCode: 400);
                }
                try
                {
                    var remoteIp = ctx.Connection.RemoteIpAddress?.MapToIPv4() ?? IPAddress.Loopback;
                    var answer = await _service.HandleRtpSessionAsync(msg, remoteIp);
                    return Results.Json(answer, jsonOpts);
                }
                catch (Exception ex)
                {
                    return Results.Text($"rtp session failed: {ex.Message}", "text/plain", statusCode: 500);
                }
            }

            if (string.IsNullOrEmpty(msg.Sdp))
            {
                return Results.Text("Invalid signaling message", "text/plain", statusCode: 400);
            }
            if (msg.Type == "offer")
            {
                var answer = await _service.HandleOfferAsync(msg);
                return answer is null
                    ? Results.Text("WebRTC init failed", "text/plain", statusCode: 500)
                    : Results.Json(answer, jsonOpts);
            }
            if (msg.Type == "answer")
            {
                return Results.Json(new { ok = true }, jsonOpts);
            }
            return Results.Text("Unknown message type", "text/plain", statusCode: 400);
        });

        app.MapPost("/ice", async (HttpContext ctx) =>
        {
            IceCandidateMessage? msg;
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                msg = JsonSerializer.Deserialize<IceCandidateMessage>(body, jsonOpts);
            }
            catch (JsonException)
            {
                msg = null;
            }
            if (msg is not null)
            {
                await _service.HandleIceCandidateAsync(msg);
            }
            return Results.Json(new { ok = true }, jsonOpts);
        });

        _app = app;
        var startTask = app.StartAsync(ct);
        Log.Info($"SignalingKestrelHost started: urls=[{string.Join(",", app.Urls)}]");
        return startTask;
    }

    /// <summary>Minimal ILoggerProvider forwarding Kestrel warnings/errors
    /// into the uniLinker file log (ClearProviders had silenced them).</summary>
    private sealed class KestrelFileLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new KestrelFileLogger(categoryName);
        public void Dispose() { }
    }

    private sealed class KestrelFileLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var text = formatter(state, exception);
            if (exception != null) text += "\n" + exception;
            UniLinker.Core.Log.Info($"[Kestrel/{level}] {categoryName}: {text}");
        }
    }

    public void Stop()
    {
        _service.Stop();
        var app = _app;
        _app = null;
        try
        {
            app?.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token)
                .Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Stopping a half-started host must never mask the real exit.
        }
    }

    public void Dispose() => Stop();
}
