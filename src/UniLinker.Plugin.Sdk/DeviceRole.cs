namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Describes the role a device plays in a connection.
/// A device can be a sender (capture + encode), receiver (decode + render), or both.
/// </summary>
[Flags]
public enum DeviceRole
{
    /// <summary>No role assigned.</summary>
    None = 0,

    /// <summary>Device captures and sends media (e.g., screen mirror source).</summary>
    Sender = 1 << 0,

    /// <summary>Device receives and renders media (e.g., screen mirror viewer).</summary>
    Receiver = 1 << 1,

    /// <summary>Device can both send and receive.</summary>
    Both = Sender | Receiver,
}
