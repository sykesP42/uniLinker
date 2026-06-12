using System.Net;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Wraps SIPSorcery's RTCPeerConnection for standard WebRTC.
/// Handles SDP exchange, ICE candidates, and H.264 video sending.
/// Any browser (Chrome, Firefox, Safari) can connect via standard WebRTC.
///
/// SIPSorcery v8 API notes:
/// - createOffer/createAnswer return SDP objects (not RTCSessionDescriptionInit)
/// - setLocalDescription/setRemoteDescription are synchronous
/// - localDescription is an SDP? (use .ToString() for the SDP text)
/// </summary>
public class PeerConnection : IDisposable
{
    private RTCPeerConnection? _pc;
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private bool _isInitialized;
    private readonly List<RTCDataChannel> _dataChannels = new();

    public string Id => _id;
    public PeerInfo RemotePeer { get; }
    public PeerConnectionState State { get; private set; } = PeerConnectionState.New;

    public event Action<PeerConnectionState>? StateChanged;
    public event Action<List<RTCIceCandidate>>? IceCandidatesGenerated;
    public event Action<RTCDataChannel>? DataChannelReceived;

    public PeerConnection(PeerInfo remotePeer)
    {
        RemotePeer = remotePeer;
    }

    /// <summary>Initialize the RTCPeerConnection with a send-only H.264 video track.</summary>
    public Task<bool> InitializeAsync()
    {
        if (_isInitialized) return Task.FromResult(true);
        if (_pc != null) return Task.FromResult(true);

        try
        {
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>() // LAN only, no STUN needed initially
            };

            _pc = new RTCPeerConnection(config);

            // Add H.264 video track (send only)
            var videoFormat = new VideoFormat(VideoCodecsEnum.H264, 96, 90000);
            var videoTrack = new MediaStreamTrack(videoFormat, MediaStreamStatusEnum.SendOnly);
            _pc.addTrack(videoTrack);

            _pc.onconnectionstatechange += state =>
            {
                State = state switch
                {
                    RTCPeerConnectionState.connected => PeerConnectionState.Connected,
                    RTCPeerConnectionState.disconnected => PeerConnectionState.Disconnected,
                    RTCPeerConnectionState.failed => PeerConnectionState.Failed,
                    RTCPeerConnectionState.closed => PeerConnectionState.Closed,
                    _ => PeerConnectionState.Connecting,
                };
                System.Diagnostics.Debug.WriteLine($"[PC:{_id}] Connection state: {State}");
                StateChanged?.Invoke(State);
            };

            _pc.onicecandidate += candidate =>
            {
                if (candidate != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[PC:{_id}] ICE candidate: {candidate}");
                    IceCandidatesGenerated?.Invoke(new List<RTCIceCandidate> { candidate });
                }
            };

            // Handle incoming DataChannel from remote peer
            _pc.ondatachannel += channel =>
            {
                if (channel != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[PC:{_id}] DataChannel received: {channel.label}");
                    _dataChannels.Add(channel);
                    DataChannelReceived?.Invoke(channel);
                }
            };

            _isInitialized = true;
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PC:{_id}] Init failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Create an SDP offer for initiating a stream.
    /// Used by the watcher (video receiver) side.
    /// </summary>
    public Task<string> CreateOfferAsync()
    {
        if (_pc == null) throw new InvalidOperationException("Not initialized");

        var offer = _pc.createOffer(new RTCOfferOptions());
        _pc.setLocalDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offer.ToString()
        });

        // Wait for ICE gathering to complete (with timeout)
        var tcs = new TaskCompletionSource<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _pc.onicegatheringstatechange += state =>
        {
            if (state == RTCIceGatheringState.complete && _pc.localDescription != null)
            {
                tcs.TrySetResult(_pc.localDescription.ToString());
            }
        };

        // Fallback after timeout
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000, cts.Token);
            var sdpText = _pc?.localDescription?.ToString() ?? "";
            tcs.TrySetResult(sdpText);
        }, cts.Token);

        return tcs.Task;
    }

    /// <summary>
    /// Wait for ICE gathering to complete (or timeout).
    /// Used by the signaling server to ensure the answer SDP
    /// includes all ICE candidates before responding to a remote offer.
    /// </summary>
    public Task WaitForIceGatheringAsync(TimeSpan timeout)
    {
        if (_pc == null) return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateChange(RTCIceGatheringState state)
        {
            if (state == RTCIceGatheringState.complete)
            {
                _pc.onicegatheringstatechange -= OnStateChange;
                tcs.TrySetResult();
            }
        }

        _pc.onicegatheringstatechange += OnStateChange;

        // Fallback timeout — if ICE never completes, proceed anyway
        _ = Task.Run(async () =>
        {
            await Task.Delay(timeout);
            _pc.onicegatheringstatechange -= OnStateChange;
            tcs.TrySetResult();
        });

        return tcs.Task;
    }

    /// <summary>
    /// Set the remote SDP from the other peer.
    /// If isOffer is true, also creates and sets a local answer.
    /// </summary>
    public Task SetRemoteDescriptionAsync(string sdp, bool isOffer)
    {
        if (_pc == null) return Task.CompletedTask;

        _pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = isOffer ? RTCSdpType.offer : RTCSdpType.answer,
            sdp = sdp
        });

        if (isOffer)
        {
            var answer = _pc.createAnswer(new RTCAnswerOptions());
            _pc.setLocalDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = answer.ToString()
            });

            var localSdp = _pc.localDescription?.ToString() ?? "";
            System.Diagnostics.Debug.WriteLine($"[PC:{_id}] Answer created:\n{localSdp}");
        }

        return Task.CompletedTask;
    }

    /// <summary>Get local SDP as a string, after setting remote description.</summary>
    public string? GetLocalSdp()
    {
        return _pc?.localDescription?.ToString();
    }

    /// <summary>Add a remote ICE candidate.</summary>
    public void AddIceCandidate(RTCIceCandidateInit candidate)
    {
        _pc?.addIceCandidate(candidate);
    }

    /// <summary>
    /// Send an encoded H.264 NAL unit via WebRTC.
    /// The buffer should be a single H.264 NAL unit (no start code prefix).
    /// Timestamp is in microseconds.
    /// </summary>
    public void SendVideoFrame(uint timestamp, byte[] h264Nal)
    {
        if (_pc == null || State != PeerConnectionState.Connected) return;

        try
        {
            _pc.SendVideo(timestamp, h264Nal);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PC:{_id}] SendVideo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a WebRTC DataChannel for reliable data transfer.
    /// </summary>
    /// <param name="label">Channel label for identification</param>
    /// <param name="ordered">Whether to guarantee order of messages (default: true)</param>
    /// <returns>The created DataChannel, or null if peer connection not ready</returns>
    public async Task<RTCDataChannel?> CreateDataChannelAsync(string label, bool ordered = true)
    {
        if (_pc == null)
        {
            System.Diagnostics.Debug.WriteLine($"[PC:{_id}] Cannot create DataChannel: not initialized");
            return null;
        }

        try
        {
            var init = new RTCDataChannelInit
            {
                ordered = ordered,
                maxRetransmits = 3  // Limited retransmits for better latency
            };

            var channel = await _pc.createDataChannel(label, init);
            if (channel != null)
            {
                _dataChannels.Add(channel);
                System.Diagnostics.Debug.WriteLine($"[PC:{_id}] DataChannel created: {label}");
            }
            return channel;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PC:{_id}] CreateDataChannel error: {ex.Message}");
            return null;
        }
    }

    public Task CloseAsync()
    {
        _pc?.close();
        _isInitialized = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var dc in _dataChannels)
        {
            try { dc.close(); } catch { }
        }
        _dataChannels.Clear();

        _pc?.close();
        _pc?.Dispose();
        _pc = null;
        _isInitialized = false;
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
