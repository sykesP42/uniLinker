namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Optional plugin hook for channels opened BY a remote peer (e.g. an
/// incoming file transfer DataChannel). Plugins that implement this get a
/// wrapped channel as soon as the remote side opens it.
/// </summary>
public interface IIncomingChannelHandler
{
    /// <summary>
    /// Handle a data channel opened by the remote peer.
    /// The implementation owns the channel afterwards.
    /// </summary>
    Task OnIncomingChannel(PeerInfo peer, IChannel channel);
}
