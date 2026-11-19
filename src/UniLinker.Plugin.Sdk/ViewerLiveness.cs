namespace UniLinker.Plugin.Sdk;

// NOTE: Log lives in the Sdk assembly under the UniLinker.Core namespace
// (plugin-facing logger) — the using below resolves to it.
using UniLinker.Core;

/// <summary>
/// Viewer liveness heartbeat. The Android receiver POSTs /ulp/keepalive
/// every 2 s while streaming; the screen-mirror plugin stamps each beat and
/// a session watchdog stops the session when beats stop arriving (killed
/// app / dead network). Transport-agnostic: works for sipsorcery, rtx and
/// tile alike because it rides the TLS pairing channel, not the media
/// socket. (A stale session used to encode and blast media into the void
/// for hours — TileEncoder emitted 17k frames for a dead viewer,
/// 2026-11-20 — because channel.OnClose only fires on an explicit
/// disconnect, which a killed receiver never sends.)
/// </summary>
public static class ViewerLiveness
{
    /// <summary>Fired (off the HTTP thread) on every viewer heartbeat.</summary>
    public static event Action? Heartbeat;

    private static long _lastBeatTicks;

    /// <summary>Record one inbound viewer signal. Any /ulp/* request from the
    /// viewer counts (keepalive, PLI, transport read) — a receiver that
    /// detects loss is demonstrably alive too.</summary>
    public static void Notify()
    {
        _lastBeatTicks = Environment.TickCount64;
        Heartbeat?.Invoke();
    }

    /// <summary>Milliseconds since the last beat (long.MaxValue when never).</summary>
    public static long MsSinceLastBeat =>
        _lastBeatTicks == 0 ? long.MaxValue : Environment.TickCount64 - _lastBeatTicks;

    public static void Reset() => _lastBeatTicks = 0;
}
