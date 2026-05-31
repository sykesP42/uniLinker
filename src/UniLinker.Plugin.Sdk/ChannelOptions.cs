namespace UniLinker.Plugin.Sdk;

public class ChannelOptions
{
    public string? Label { get; init; }
    public bool Ordered { get; init; } = true;
    public int MaxRetransmits { get; init; } = 0;
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
