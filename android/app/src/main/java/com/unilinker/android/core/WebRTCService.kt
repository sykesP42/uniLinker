package com.unilinker.android.core

import android.util.Log
import com.unilinker.android.sdk.IPeerMesh
import com.unilinker.android.sdk.PeerConnectionState
import com.unilinker.android.sdk.models.PeerDevice
import com.unilinker.android.sdk.models.StreamStats
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import org.webrtc.*
import java.util.concurrent.Executors

class WebRTCService(
    private val signalingUrl: String,
    private val peerId: String = "android-client"
) : IPeerMesh {

    private val executor = Executors.newSingleThreadExecutor()
    private val eglBase = EglBase.create()
    val eglBaseContext: EglBase.Context get() = eglBase.eglBaseContext
    private var peerConnectionFactory: PeerConnectionFactory? = null
    private var peerConnection: PeerConnection? = null
    private var videoTrack: VideoTrack? = null

    private val okHttpClient = OkHttpClient.Builder()
        .connectTimeout(10, java.util.concurrent.TimeUnit.SECONDS)
        .readTimeout(30, java.util.concurrent.TimeUnit.SECONDS)
        .build()

    override val connectedPeers = MutableStateFlow<List<PeerDevice>>(emptyList())
    override val connectionState = MutableStateFlow(PeerConnectionState.IDLE)
    override val stats = MutableStateFlow(StreamStats())

    private var statsCollectorJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.Default + SupervisorJob())

    fun onVideoTrackReady(callback: (VideoTrack) -> Unit) {
        this.videoTrackCallback = callback
    }

    private var videoTrackCallback: ((VideoTrack) -> Unit)? = null

    @Volatile
    private var gatheringComplete: Boolean = false

    @Volatile
    private var factoryInitialized: Boolean = false

    fun initialize() {
        if (factoryInitialized) return
        val options = PeerConnectionFactory.InitializationOptions
            .builder(context)
            .setFieldTrials("WebRTC-H264HighProfile/Enabled/")
            .createInitializationOptions()
        PeerConnectionFactory.initialize(options)

        val encoderFactory = DefaultVideoEncoderFactory(eglBase.eglBaseContext, true, true)
        val decoderFactory = DefaultVideoDecoderFactory(eglBase.eglBaseContext)

        peerConnectionFactory = PeerConnectionFactory.builder()
            .setVideoEncoderFactory(encoderFactory)
            .setVideoDecoderFactory(decoderFactory)
            .setOptions(PeerConnectionFactory.Options().apply {
                disableEncryption = false
                disableNetworkMonitor = true
            })
            .createPeerConnectionFactory()

        factoryInitialized = true
    }

    override fun connect(peer: PeerDevice) {
        connectionState.value = PeerConnectionState.CONNECTING
        gatheringComplete = false
        createPeerConnection()
        if (peerConnection == null) {
            Log.e(TAG, "createPeerConnection returned null - cannot proceed")
            connectionState.value = PeerConnectionState.ERROR
            return
        }
        Log.i(TAG, "PeerConnection created, creating offer")
        createOffer()
    }

    private fun createPeerConnection() {
        // No STUN servers. uniLinker is a LAN-only connector (C2/C3): host
        // candidates cover same-subnet Wi-Fi, hotspot and Wi-Fi Direct, while a
        // public STUN URL would send traffic outside the LAN and stall ICE
        // gathering until it times out. See C-22.
        val rtcConfig = PeerConnection.RTCConfiguration(emptyList()).apply {
            tcpCandidatePolicy = PeerConnection.TcpCandidatePolicy.DISABLED
        }

        val observer = object : PeerConnection.Observer {
            override fun onIceCandidate(candidate: IceCandidate) {
                sendIceCandidate(candidate)
            }

            override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>?) {}
            override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                when (state) {
                    PeerConnection.IceConnectionState.CONNECTED ->
                        connectionState.value = PeerConnectionState.CONNECTED
                    PeerConnection.IceConnectionState.DISCONNECTED ->
                        connectionState.value = PeerConnectionState.DISCONNECTED
                    PeerConnection.IceConnectionState.FAILED ->
                        connectionState.value = PeerConnectionState.ERROR
                    else -> {}
                }
            }

            override fun onAddStream(stream: MediaStream?) {}
            override fun onRemoveStream(stream: MediaStream?) {}
            override fun onDataChannel(channel: DataChannel?) {}
            override fun onRenegotiationNeeded() {}

            override fun onAddTrack(receiver: RtpReceiver?, streams: Array<out MediaStream>?) {
                val track = receiver?.track()
                if (track is VideoTrack) {
                    videoTrack = track
                    videoTrackCallback?.invoke(track)
                    startStatsCollection()
                }
            }

            override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) {
                if (state == PeerConnection.IceGatheringState.COMPLETE) {
                    gatheringComplete = true
                }
            }
            override fun onSignalingChange(state: PeerConnection.SignalingState?) {}
            override fun onIceConnectionReceivingChange(receiving: Boolean) {}
        }

        peerConnection = peerConnectionFactory?.createPeerConnection(rtcConfig, observer)
    }

    private fun createOffer() {
        val pc = peerConnection ?: return
        val constraints = MediaConstraints().apply {
            mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"))
        }

        pc.createOffer(object : SdpObserver {
            override fun onCreateSuccess(sdp: SessionDescription) {
                // setLocalDescription triggers ICE gathering. The observer
                // passed HERE is the one whose onSetSuccess fires when the
                // local description lands - send the offer from there after
                // gathering completes (with a fallback so a stalled gatherer
                // cannot block signaling). Trickle candidates keep flowing
                // via /ice regardless. (The createOffer observer's own
                // onSetSuccess never fires for setLocalDescription - that
                // was the original silent-send bug.)
                pc.setLocalDescription(object : SdpObserver {
                    override fun onSetSuccess() {
                        scope.launch {
                            withTimeoutOrNull(1500) {
                                while (gatheringComplete != true) delay(50)
                            }
                            pc.localDescription?.let { sendSdp(it) }
                        }
                    }
                    override fun onCreateSuccess(p0: SessionDescription?) {}
                    override fun onSetFailure(error: String?) {
                        Log.e(TAG, "setLocalDescription failed: $error")
                        scope.launch { connectionState.value = PeerConnectionState.ERROR }
                    }
                    override fun onCreateFailure(error: String?) {
                        Log.e(TAG, "local description creation failed: $error")
                    }
                }, sdp)
            }
            override fun onSetSuccess() {}
            override fun onCreateFailure(error: String) {
                Log.e(TAG, "createOffer failed: $error")
                connectionState.value = PeerConnectionState.ERROR
            }
            override fun onSetFailure(error: String) {}
        }, constraints)
    }

    private fun sendSdp(sdp: SessionDescription) {
        val json = JSONObject().apply {
            put("type", sdp.type.canonicalForm())
            put("sdp", sdp.description)
            // The Windows server reads `fromPeerId` (camelCase) and matches
            // /ice candidates against it; `peerId` alone is ignored there.
            put("fromPeerId", peerId)
            put("capability", "screen-capture")
            put("peerId", peerId)
        }
        Log.i(TAG, "Sending offer to $signalingUrl/signaling")
        postJson("/signaling", json) { answerJson ->
            Log.i(TAG, "Answer received, applying remote description")
            val remoteSdp = SessionDescription(
                SessionDescription.Type.fromCanonicalForm(answerJson.getString("type")),
                answerJson.getString("sdp"),
            )
            peerConnection?.setRemoteDescription(SimpleSdpObserver(), remoteSdp)
        }
    }

    private fun sendIceCandidate(candidate: IceCandidate) {
        val json = JSONObject().apply {
            put("type", "candidate")
            put("candidate", candidate.sdp)
            put("sdpMid", candidate.sdpMid)
            put("sdpMLineIndex", candidate.sdpMLineIndex)
            put("peerId", peerId)
        }
        postJson("/ice", json) {}
    }

    private fun postJson(path: String, json: JSONObject, onResponse: (JSONObject) -> Unit) {
        val url = "$signalingUrl$path"
        val body = json.toString().toRequestBody("application/json".toMediaType())

        okHttpClient.newCall(Request.Builder().url(url).post(body).build())
            .enqueue(object : Callback {
            override fun onFailure(call: Call, e: java.io.IOException) {
                Log.e(TAG, "POST $path failed: ${e.message}")
                if (path == "/signaling") {
                    scope.launch {
                        connectionState.value = PeerConnectionState.ERROR
                    }
                }
            }
                override fun onResponse(call: Call, response: Response) {
                    response.body?.string()?.let { body ->
                        if (body.isNotEmpty()) {
                            try { onResponse(JSONObject(body)) }
                            catch (_: Exception) {}
                        }
                    }
                }
            })
    }

    private fun startStatsCollection() {
        statsCollectorJob?.cancel()
        statsCollectorJob = scope.launch {
            delay(2000)
            while (isActive && connectionState.value == PeerConnectionState.CONNECTED) {
                peerConnection?.getStats { reports ->
                    for (report in (reports as Map<String, RTCStats>).values) {
                        if (report.type == "inbound-rtp" && report.members["kind"] == "video") {
                            val w = (report.members["frameWidth"] as? String)?.toIntOrNull() ?: 0
                            val h = (report.members["frameHeight"] as? String)?.toIntOrNull() ?: 0
                            val fps = (report.members["framesPerSecond"] as? String)?.toIntOrNull() ?: 0
                            val dMs = (report.members["totalDecodeTime"] as? String)
                                ?.toFloatOrNull()?.div(1000f) ?: 0f
                            stats.value = StreamStats(width = w, height = h, fps = fps, decodeMs = dMs)
                        }
                    }
                }
                delay(1000)
            }
        }
    }

    override fun disconnect() {
        statsCollectorJob?.cancel()
        peerConnection?.close()
        peerConnection = null
        connectionState.value = PeerConnectionState.IDLE
    }

    override fun dispose() {
        disconnect()
        peerConnectionFactory?.dispose()
    }

    private class SimpleSdpObserver : SdpObserver {
        override fun onCreateSuccess(p0: SessionDescription?) {}
        override fun onSetSuccess() {
            Log.i(TAG, "Remote description applied")
        }
        override fun onCreateFailure(p0: String?) {}
        override fun onSetFailure(error: String?) {
            Log.e(TAG, "Remote description failed: $error")
        }
    }

    companion object {
        private const val TAG = "UniLinkerWebRTC"
    }

    private val context: android.content.Context
        get() = org.webrtc.ContextUtils.getApplicationContext()
}
