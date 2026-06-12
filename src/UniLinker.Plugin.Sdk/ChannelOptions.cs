namespace UniLinker.Plugin.Sdk;

public class ChannelOptions
{
    public string? Label { get; init; }
    public bool Ordered { get; init; } = true;
    public int MaxRetransmits { get; init; } = 0;

    /// <summary>
    /// Type of channel to create. Default is MediaTrack (video/audio).
    /// Set to DataChannel for reliable data transfer.
    /// </summary>
    public ChannelType Type { get; init; } = ChannelType.MediaTrack;
}

public enum ChannelType
{
    MediaTrack,
    DataChannel
}

public enum ChannelState
{
    Connecting,
    Open,
    Closing,
    Closed
}
