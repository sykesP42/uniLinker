namespace UniLinker.Plugin.Sdk;

public class PeerInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Version { get; init; } = string.Empty;
    public string[] Capabilities { get; init; } = [];
    public PeerState State { get; set; } = PeerState.Discovered;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public enum PeerState
{
    Discovered,
    Connected,
    Disconnected
}
