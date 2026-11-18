namespace UniLinker.Core;

/// <summary>
/// Which sender implementation serves the direct-RTP tier. Selected in
/// Settings ("Screen mirror transport"); the screen-mirror plugin publishes
/// the setting here at Initialize, the signaling service reads it when a
/// receiver requests an rtp session.
/// </summary>
public static class DirectRtpTransport
{
    /// <summary>"legacy" (default) = SIPSorcery RTPSession SDES path —
    /// proven steady 30-50fps, no RTX (degrades under sustained loss);
    /// "rtx" = own-pipeline sender with NACK retransmission (M5) — lower
    /// latency under loss but the reorder pipeline still has a
    /// steady-state pin bug under investigation. Anything else = legacy.</summary>
    public static string Mode { get; set; } = "legacy";
}
