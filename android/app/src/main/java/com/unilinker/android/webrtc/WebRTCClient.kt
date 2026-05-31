package com.unilinker.android.webrtc

import android.util.Log
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import org.webrtc.*
import java.util.concurrent.Executors

enum class ConnectionState {
    IDLE, DISCOVERING, CONNECTING, CONNECTED, DISCONNECTED, ERROR
}

data class StreamStats(
    val width: Int = 0,
    val height: Int = 0,
    val fps: Int = 0,
    val bitrateKbps: Int = 0,
    val decodeMs: Float = 0f,
)

class WebRTCClient(
    private val signalingUrl: String,
) {
    companion object {
        private const val TAG = "UniLinkerWebRTC"
    }

    private val executor = Executors.newSingleThreadExecutor()
    private val eglBase = EglBase.create()
    private var peerConnectionFactory: PeerConnectionFactory? = null
    private var peerConnection: PeerConnection? = null
    private var videoTrack: VideoTrack? = null

    private val okHttpClient = OkHttpClient.Builder()
        .connectTimeout(10, java.util.concurrent.TimeUnit.SECONDS)
        .readTimeout(30, java.util.concurrent.TimeUnit.SECONDS)
        .build()

    private val _connectionState = MutableStateFlow(ConnectionState.IDLE)
    val connectionState: StateFlow<ConnectionState> = _connectionState

    private val _stats = MutableStateFlow(StreamStats())
    val stats: StateFlow<StreamStats> = _stats

    private val _remoteVideoTrack = MutableSharedFlow<VideoTrack>(replay = 1)
    val remoteVideoTrack: SharedFlow<VideoTrack> = _remoteVideoTrack

    private var statsCollectorJob: Job? = null

    fun initialize() {
        val options = PeerConnectionFactory.InitializationOptions.builder(context)
            .setFieldTrials("WebRTC-H264HighProfile/Enabled/")
            .createInitializationOptions()
        PeerConnectionFactory.initialize(options)

        val encoderFactory = DefaultVideoEncoderFactory(
            eglBase.eglBaseContext, true, true
        )
        val decoderFactory = DefaultVideoDecoderFactory(eglBase.eglBaseContext)

        peerConnectionFactory = PeerConnectionFactory.builder()
            .setVideoEncoderFactory(encoderFactory)
            .setVideoDecoderFactory(decoderFactory)
            .setOptions(PeerConnectionFactory.Options().apply {
                disableEncryption = false
                disableNetworkMonitor = true
            })
            .createPeerConnectionFactory()
    }

    fun connect() {
        _connectionState.value = ConnectionState.CONNECTING
        createPeerConnection()
        createOffer()
    }

    private fun createPeerConnection() {
        val rtcConfig = PeerConnection.RTCConfiguration(
            listOf(
                PeerConnection.IceServer.builder("stun:stun.l.google.com:19302").createIceServer()
            )
        ).apply {
            tcpCandidatePolicy = PeerConnection.TcpCandidatePolicy.DISABLED
            continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_ONCE
        }

        val observer = object : PeerConnection.Observer {
            override fun onIceCandidate(candidate: IceCandidate) {
                sendIceCandidate(candidate)
            }

            override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>?) {}
            override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                Log.d(TAG, "ICE state: $state")
                when (state) {
                    PeerConnection.IceConnectionState.CONNECTED ->
                        _connectionState.value = ConnectionState.CONNECTED
                    PeerConnection.IceConnectionState.DISCONNECTED ->
                        _connectionState.value = ConnectionState.DISCONNECTED
                    PeerConnection.IceConnectionState.FAILED ->
                        _connectionState.value = ConnectionState.ERROR
                    else -> {}
                }
            }

            override fun onAddStream(stream: MediaStream?) {}
            override fun onRemoveStream(stream: MediaStream?) {}
            override fun onDataChannel(channel: DataChannel?) {}
            override fun onRenegotiationNeeded() {}
            override fun onAddTrack(receiver: RtpReceiver?, streams: Array<out MediaStream>?) {
                Log.d(TAG, "Track received: ${receiver?.track()?.kind()}")
                val track = receiver?.track()
                if (track is VideoTrack) {
                    videoTrack = track
                    _remoteVideoTrack.tryEmit(track)
                    startStatsCollection()
                }
            }
            override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) {}
            override fun onSignalingChange(state: PeerConnection.SignalingState?) {}
            override fun onIceConnectionReceivingChange(receiving: Boolean) {}
        }

        peerConnection = peerConnectionFactory?.createPeerConnection(rtcConfig, observer)
        Log.d(TAG, "PeerConnection created")
    }

    private fun createOffer() {
        val pc = peerConnection ?: return

        // Request H.264 video
        val constraints = MediaConstraints().apply {
            mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"))
        }

        pc.createOffer(object : SdpObserver {
            override fun onCreateSuccess(sessionDescription: SessionDescription) {
                pc.setLocalDescription(object : SdpObserver {
                    override fun onSetSuccess() {
                        sendSdp(sessionDescription)
                    }
                    override fun onSetFailure(error: String) {
                        Log.e(TAG, "setLocalDescription failed: $error")
                        _connectionState.value = ConnectionState.ERROR
                    }
                    override fun onCreateSuccess(p0: SessionDescription?) {}
                    override fun onCreateFailure(p0: String?) {}
                }, sessionDescription)
            }

            override fun onSetSuccess() {}
            override fun onCreateFailure(error: String) {
                Log.e(TAG, "createOffer failed: $error")
                _connectionState.value = ConnectionState.ERROR
            }

            override fun onSetFailure(error: String) {}
        }, constraints)
    }

    private fun sendSdp(sdp: SessionDescription) {
        val json = JSONObject().apply {
            put("type", sdp.type.canonicalForm())
            put("sdp", sdp.description)
        }

        postJson("/signaling", json) { answerJson ->
            val answerType = answerJson.getString("type")
            val answerSdp = answerJson.getString("sdp")
            val remoteSdp = SessionDescription(
                SessionDescription.Type.fromCanonicalForm(answerType),
                answerSdp,
            )

            peerConnection?.setRemoteDescription(object : SdpObserver {
                override fun onSetSuccess() {
                    Log.d(TAG, "Remote description set")
                }
                override fun onSetFailure(error: String) {
                    Log.e(TAG, "setRemoteDescription failed: $error")
                }
                override fun onCreateSuccess(p0: SessionDescription?) {}
                override fun onCreateFailure(p0: String?) {}
            }, remoteSdp)
        }
    }

    private fun sendIceCandidate(candidate: IceCandidate) {
        val json = JSONObject().apply {
            put("type", "candidate")
            put("candidate", candidate.sdp)
            put("sdpMid", candidate.sdpMid)
            put("sdpMLineIndex", candidate.sdpMLineIndex)
        }

        postJson("/ice", json) { /* no response needed */ }
    }

    private fun postJson(path: String, json: JSONObject, onResponse: (JSONObject) -> Unit) {
        val url = "$signalingUrl$path"
        val body = json.toString().toRequestBody("application/json".toMediaType())

        val request = Request.Builder()
            .url(url)
            .post(body)
            .build()

        okHttpClient.newCall(request).enqueue(object : Callback {
            override fun onFailure(call: Call, e: java.io.IOException) {
                Log.e(TAG, "POST $path failed: ${e.message}")
                if (_connectionState.value == ConnectionState.CONNECTING) {
                    _connectionState.value = ConnectionState.ERROR
                }
            }

            override fun onResponse(call: Call, response: Response) {
                val body = response.body?.string()
                if (body != null && body.isNotEmpty()) {
                    try {
                        val json = JSONObject(body)
                        onResponse(json)
                    } catch (e: Exception) {
                        Log.w(TAG, "Invalid JSON response: $body")
                    }
                }
            }
        })
    }

    private fun startStatsCollection() {
        statsCollectorJob?.cancel()
        statsCollectorJob = CoroutineScope(Dispatchers.Default).launch {
            delay(2000) // Wait for connection to stabilize
            while (isActive && _connectionState.value == ConnectionState.CONNECTED) {
                peerConnection?.getStats { reports ->
                    for (report in reports) {
                        if (report.type == "inbound-rtp" && report.members["kind"] == "video") {
                            val width = (report.members["frameWidth"] as? String)?.toIntOrNull() ?: 0
                            val height = (report.members["frameHeight"] as? String)?.toIntOrNull() ?: 0
                            val fps = (report.members["framesPerSecond"] as? String)?.toIntOrNull() ?: 0
                            val decodeMs = (report.members["totalDecodeTime"] as? String)
                                ?.toFloatOrNull()?.div(1000f) ?: 0f
                            val br = (report.members["bytesReceived"] as? String)?.toLongOrNull() ?: 0L

                            _stats.value = StreamStats(
                                width = width,
                                height = height,
                                fps = fps,
                                decodeMs = decodeMs,
                            )
                        }
                    }
                }
                delay(1000)
            }
        }
    }

    fun disconnect() {
        statsCollectorJob?.cancel()
        peerConnection?.close()
        peerConnection = null
        _connectionState.value = ConnectionState.DISCONNECTED
    }

    fun dispose() {
        disconnect()
        peerConnectionFactory?.dispose()
    }

    // Required by WebRTC initialization
    private val context: android.content.Context
        get() = org.webrtc.ContextUtils.getApplicationContext()
}
