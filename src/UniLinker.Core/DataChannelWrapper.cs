using SIPSorcery.Net;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Wraps SIPSorcery's RTCDataChannel to implement IChannel interface.
/// Used for reliable data transfer (file transfer, messages, etc.).
/// </summary>
public class DataChannelWrapper : IChannel
{
    private readonly RTCDataChannel _dataChannel;
    private readonly PeerInfo _remotePeer;
    private readonly string _capability;
    private readonly string _id;
    private bool _disposed;

    public string Id => _id;
    public ChannelType Type => ChannelType.DataChannel;
    public PeerInfo RemotePeer => _remotePeer;
    public string Capability => _capability;
    public bool IsOpen => _dataChannel?.readyState == RTCDataChannelState.open && !_disposed;

    public ChannelState State => _dataChannel?.readyState switch
    {
        RTCDataChannelState.connecting => ChannelState.Connecting,
        RTCDataChannelState.open => ChannelState.Open,
        RTCDataChannelState.closing => ChannelState.Closing,
        _ => ChannelState.Closed
    };

    public event Action? OnClose;
    public event Action<byte[]>? MessageReceived;
    public event Action<EncodedPacket>? PacketReceived;

    public DataChannelWrapper(RTCDataChannel dataChannel, PeerInfo remotePeer, string capability)
    {
        _dataChannel = dataChannel ?? throw new ArgumentNullException(nameof(dataChannel));
        _remotePeer = remotePeer;
        _capability = capability;
        _id = $"{remotePeer.Id}:{capability}:{Guid.NewGuid().ToString("N")[..8]}";

        // Wire up events
        _dataChannel.onopen += () =>
        {
            System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Channel opened");
        };

        _dataChannel.onclose += () =>
        {
            System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Channel closed");
            OnClose?.Invoke();
        };

        _dataChannel.onerror += (error) =>
        {
            System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Error: {error}");
        };

        // Handle incoming messages
        // OnDataChannelMessageDelegate signature: (RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
        _dataChannel.onmessage += (dc, protocol, data) =>
        {
            if (data != null && data.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Received {data.Length} bytes, protocol: {protocol}");
                MessageReceived?.Invoke(data);
            }
        };
    }

    /// <summary>
    /// Send raw bytes through the DataChannel.
    /// </summary>
    public Task SendAsync(byte[] data)
    {
        if (_disposed || !IsOpen)
        {
            System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Cannot send: channel not open");
            return Task.CompletedTask;
        }

        try
        {
            _dataChannel.send(data);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Send error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Send an encoded packet. For DataChannel, this sends raw packet data.
    /// </summary>
    public Task SendPacketAsync(EncodedPacket packet)
    {
        // For DataChannel, we just send the raw data
        // The packet structure is preserved for protocol compatibility
        return SendAsync(packet.Data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _dataChannel.close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DC:{_id}] Dispose error: {ex.Message}");
        }

        OnClose?.Invoke();
    }
}
