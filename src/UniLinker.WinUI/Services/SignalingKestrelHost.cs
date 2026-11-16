using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UniLinker.Core;

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
    private WebApplication? _app;

    public int Port => _port;
    public bool IsRunning => _app is not null;

    public SignalingKestrelHost(int port, PeerMesh peerMesh)
    {
        _port = port;
        _service = new SignalingService(peerMesh, port);
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
        builder.WebHost.UseUrls($"http://*:{_port}");

        var app = builder.Build();

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

            if (msg is null || string.IsNullOrEmpty(msg.Sdp))
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
        return app.StartAsync(ct);
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
