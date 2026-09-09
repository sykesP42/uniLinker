namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Configuration for creating a WebRTC channel.
/// Passed to <see cref="IPeerMesh.CreateChannel"/>.
/// </summary>
public class ChannelOptions
{
    /// <summary>Optional label for identifying the channel (e.g., "screen-mirror-1").</summary>
    public string? Label { get; init; }

    /// <summary>Whether messages are delivered in order (default: true).</summary>
    public bool Ordered { get; init; } = true;

    /// <summary>
    /// Maximum retransmit attempts for DataChannels (0 = unlimited).
    /// Set to a small value for low-latency, lossy channels.
    /// </summary>
    public int MaxRetransmits { get; init; } = 0;

    /// <summary>
    /// Type of channel to create. Default is MediaTrack (video/audio).
    /// Set to DataChannel for reliable data transfer.
    /// </summary>
    public ChannelType Type { get; init; } = ChannelType.MediaTrack;
}

/// <summary>WebRTC channel transport type.</summary>
public enum ChannelType
{
    /// <summary>Media track for streaming encoded video/audio.</summary>
    MediaTrack,

    /// <summary>Reliable (or semi-reliable) data channel for binary/text messages.</summary>
    DataChannel
}

/// <summary>Lifecycle state of a channel.</summary>
public enum ChannelState
{
    /// <summary>Channel is being established.</summary>
    Connecting,

    /// <summary>Channel is open and ready for data transfer.</summary>
    Open,

    /// <summary>Channel is gracefully closing.</summary>
    Closing,

    /// <summary>Channel is closed.</summary>
    Closed
}
