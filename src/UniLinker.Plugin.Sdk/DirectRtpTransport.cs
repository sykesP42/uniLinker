namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Which sender implementation serves the direct-RTP tier, named after the
/// core technology stack each mode is built on. Selected in Settings
/// ("Screen mirror transport"); persisted in settings.json (TransportIndex).
/// </summary>
public static class DirectRtpTransport
{
    /// <summary>"sipsorcery" (default) = the SIPSorcery RTPSession SDES
    /// pipeline — proven steady 30-50fps, no RTX (degrades under sustained
    /// loss); "rtx" = Native RTX — own-pipeline RFC 6184 packetizer +
    /// SIPSorcery SrtpTransformEngine + NACK retransmission (M5): lower
    /// latency under loss, but the reorder pipeline still has a
    /// steady-state pin bug under investigation. Anything else = sipsorcery.</summary>
    public static string Mode { get; set; } = "sipsorcery";
}
