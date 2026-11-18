namespace UniLinker.Plugin.Sdk;

// NOTE: Log lives in the Sdk assembly under the UniLinker.Core namespace
// (plugin-facing logger) — the using below resolves to it.
using UniLinker.Core;

/// <summary>
/// Media-plane key-frame request (PLI) broker. The Android receiver detects
/// RTP sequence gaps and asks the sender for an IDR instead of waiting for
/// the periodic GOP boundary (up to 2 s of artifacts). Transport maps the
/// HTTPS /ulp/pli endpoint onto <see cref="Request"/>; the screen-mirror
/// plugin subscribes to <see cref="KeyFrameRequested"/> and forces the IDR.
/// Rate-limited on this side too — a receiver flood must not force an IDR
/// storm (each IDR is a multi-hundred-KB burst).
/// </summary>
public static class KeyFrameBroker
{
    private static readonly object Gate = new();
    private static long _lastHonoredTicks;
    private static int _honored;
    private static int _received;

    /// <summary>Minimum spacing between honored requests. One IDR per 250 ms
    /// bounds the burst rate while recovering within the ≤1 s chaos budget.</summary>
    public static int MinIntervalMs { get; set; } = 250;

    /// <summary>Fired (off the HTTP thread) when a key frame should be forced.</summary>
    public static event Action? KeyFrameRequested;

    /// <summary>Rate-limited request entry point. Returns true when honored.</summary>
    public static bool Request(string requester)
    {
        bool honored;
        lock (Gate)
        {
            _received++;
            var now = Environment.TickCount64;
            honored = now - _lastHonoredTicks >= MinIntervalMs;
            if (honored)
            {
                _lastHonoredTicks = now;
                _honored++;
            }
        }
        if (honored)
        {
            Log.Info($"[PLI] key frame requested by {requester} (honored {_honored}/{_received})");
            KeyFrameRequested?.Invoke();
        }
        return honored;
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _lastHonoredTicks = 0;
            _honored = 0;
            _received = 0;
        }
    }
}
