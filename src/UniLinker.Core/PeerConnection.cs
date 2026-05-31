using System.Net;
using System.Net.Sockets;
using System.Text;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Minimal LAN peer connection: SDP exchange + RTP over UDP.
/// No ICE/STUN/TURN — direct UDP on known IP:port.
/// </summary>
public class PeerConnection : IDisposable
{
    private UdpClient? _rtpClient;
    private IPEndPoint? _remoteEndPoint;
    private RtpPacketizer? _packetizer;
    private UdpClient? _rtpListener;
    private int _localRtpPort;
    private int _localRtcpPort;
    private CancellationTokenSource? _listenCts;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public PeerInfo RemotePeer { get; }
    public PeerConnectionState State { get; private set; } = PeerConnectionState.New;
    public int LocalRtpPort => _localRtpPort;
    public IPEndPoint? RemoteEndPoint => _remoteEndPoint;

    public event Action<byte[]>? RtpPacketReceived;
    public event Action<PeerConnectionState>? StateChanged;

    public PeerConnection(PeerInfo remotePeer)
    {
        RemotePeer = remotePeer;
    }

    /// <summary>Directly set the remote endpoint without parsing SDP.</summary>
    public void SetRemoteEndPoint(string ip, int port)
    {
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
    }

    /// <summary>Directly set the remote endpoint from an IPEndPoint.</summary>
    public void SetRemoteEndPoint(IPEndPoint endpoint)
    {
        _remoteEndPoint = endpoint;
    }

    /// <summary>Generate an SDP offer to initiate a media stream.</summary>
    public string CreateOffer()
    {
        _localRtpPort = GetRandomPort();
        _localRtcpPort = _localRtpPort + 1;
        var localIp = GetLocalIP();

        var sb = new StringBuilder();
        sb.AppendLine("v=0");
        sb.AppendLine($"o=- {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} 1 IN IP4 {localIp}");
        sb.AppendLine("s=UniLinker Stream");
        sb.AppendLine($"c=IN IP4 {localIp}");
        sb.AppendLine("t=0 0");
        sb.AppendLine($"m=video {_localRtpPort} RTP/AVP 96");
        sb.AppendLine("a=rtpmap:96 H264/90000");
        sb.AppendLine("a=fmtp:96 profile-level-id=640028; packetization-mode=1");
        sb.AppendLine("a=recvonly");
        sb.AppendLine($"a=rtcp:{_localRtcpPort}");
        return sb.ToString();
    }

    /// <summary>Create an SDP answer in response to an offer.</summary>
    public string CreateAnswer()
    {
        _localRtpPort = GetRandomPort();
        _localRtcpPort = _localRtpPort + 1;
        var localIp = GetLocalIP();

        var sb = new StringBuilder();
        sb.AppendLine("v=0");
        sb.AppendLine($"o=- {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} 1 IN IP4 {localIp}");
        sb.AppendLine("s=UniLinker Stream");
        sb.AppendLine($"c=IN IP4 {localIp}");
        sb.AppendLine("t=0 0");
        sb.AppendLine($"m=video {_localRtpPort} RTP/AVP 96");
        sb.AppendLine("a=rtpmap:96 H264/90000");
        sb.AppendLine("a=fmtp:96 profile-level-id=640028; packetization-mode=1");
        sb.AppendLine("a=sendonly");
        sb.AppendLine($"a=rtcp:{_localRtcpPort}");
        return sb.ToString();
    }

    /// <summary>Parse remote SDP and extract connection info.</summary>
    public void ParseRemoteSdp(string sdp)
    {
        string? remoteIp = null;
        int? remotePort = null;

        foreach (var line in sdp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("c=IN IP4 "))
                remoteIp = trimmed["c=IN IP4 ".Length..];
            else if (trimmed.StartsWith("m=video "))
            {
                var parts = trimmed.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[1], out var port))
                    remotePort = port;
            }
        }

        if (remoteIp != null && remotePort.HasValue)
        {
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort.Value);
        }
    }

    /// <summary>Start the sender side: create UDP socket and RTP packetizer.</summary>
    public async Task StartSendingAsync()
    {
        _packetizer = new RtpPacketizer();
        _rtpClient = new UdpClient();
        SetState(PeerConnectionState.Connected);
        await Task.CompletedTask;
    }

    /// <summary>Send an encoded H.264 packet as RTP over UDP.</summary>
    public async Task SendEncodedPacketAsync(EncodedPacket packet)
    {
        if (_rtpClient == null || _remoteEndPoint == null || _packetizer == null)
            return;

        var rtpPackets = _packetizer.Packetize(packet.Data, packet.TimestampUs);
        foreach (var rtp in rtpPackets)
        {
            await _rtpClient.SendAsync(rtp, rtp.Length, _remoteEndPoint);
        }
    }

    /// <summary>Start receiving RTP on a local port.</summary>
    public void StartReceiving(int localPort)
    {
        _listenCts = new CancellationTokenSource();
        _rtpListener = new UdpClient(localPort);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_listenCts.IsCancellationRequested)
                {
                    var result = await _rtpListener.ReceiveAsync(_listenCts.Token);
                    RtpPacketReceived?.Invoke(result.Buffer);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        });
    }

    public void StopReceiving()
    {
        _listenCts?.Cancel();
        _rtpListener?.Dispose();
        _rtpListener = null;
    }

    public void Dispose()
    {
        StopReceiving();
        _rtpClient?.Dispose();
        SetState(PeerConnectionState.Closed);
    }

    private void SetState(PeerConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static int GetRandomPort() => Random.Shared.Next(20000, 50000);

    private static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(ip))
                return ip.ToString();
        }
        return "127.0.0.1";
    }
}

public enum PeerConnectionState
{
    New,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Closed,
}
