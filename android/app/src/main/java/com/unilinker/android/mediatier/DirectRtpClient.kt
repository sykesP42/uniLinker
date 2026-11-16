package com.unilinker.android.mediatier

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.Base64
import java.util.concurrent.TimeUnit

/**
 * Control plane for the direct-RTP tier: POST /signaling {type:"rtp"} with
 * OUR UDP listen port; the answer carries the sender's SDES crypto line and
 * SSRC. Spawns a DirectRtpReceiver bound on the advertised port and exposes
 * decoded-ready Annex-B access units as a flow.
 */
/** Receiver-driven capture tier: carried in the rtp request, honored by the
 *  sender (values 0 mean "sender default"). */
data class CaptureTier(
    val width: Int,
    val height: Int,
    val bitrateKbps: Int,
    val fps: Int,
)

class DirectRtpClient(private val host: String, private val signalingPort: Int) {
    enum class State { IDLE, CONNECTING, STREAMING, ERROR }

    private val http = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    private val _state = MutableStateFlow(State.IDLE)
    val state: StateFlow<State> = _state.asStateFlow()

    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error.asStateFlow()

    private val _frames = MutableSharedFlow<ByteArray>(extraBufferCapacity = 8, onBufferOverflow = kotlinx.coroutines.channels.BufferOverflow.DROP_OLDEST)
    val frames: SharedFlow<ByteArray> = _frames.asSharedFlow()

    @Volatile private var receiver: DirectRtpReceiver? = null

    /** Latest receiver counters, for the HUD (null while not streaming). */
    val receiverStats: Triple<Int, Int, Long>?
        get() = receiver?.let { Triple(it.receivedPackets, it.lostPackets, it.bytesReceived) }

    /** Bind first (so the OS picks a free UDP port), then announce it. */
    suspend fun connect(tier: CaptureTier): Boolean = withContext(Dispatchers.IO) {
        _state.value = State.CONNECTING
        _error.value = null

        val listenSocket = java.net.DatagramSocket(null).apply {
            reuseAddress = true
            bind(java.net.InetSocketAddress(0))
        }
        val localPort = listenSocket.localPort
        listenSocket.close() // receiver re-binds deterministically on the same port

        val body = JSONObject().apply {
            put("type", "rtp")
            put("fromPeerId", "android-direct")
            put("capability", "screen-capture")
            put("rtpPort", localPort)
            // Receiver-driven tier (0 values = sender defaults).
            put("captureWidth", tier.width)
            put("captureHeight", tier.height)
            put("bitrateKbps", tier.bitrateKbps)
            put("captureFps", tier.fps)
        }.toString()

        try {
            val request = Request.Builder()
                .url("http://$host:$signalingPort/signaling")
                .post(body.toRequestBody("application/json".toMediaType()))
                .build()
            http.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    _error.value = "signaling ${response.code}"
                    _state.value = State.ERROR
                    return@withContext false
                }
                val answer = JSONObject(response.body!!.string())
                val crypto = answer.optString("crypto", "")
                val ssrc = answer.optLong("ssrc", 0)
                if (!crypto.startsWith("1 AES_CM_128_HMAC_SHA1_80")) {
                    _error.value = "unsupported crypto suite"
                    _state.value = State.ERROR
                    return@withContext false
                }
                val inline = crypto.substringAfter("inline:", "")
                if (inline.isEmpty()) {
                    _error.value = "crypto line missing key"
                    _state.value = State.ERROR
                    return@withContext false
                }
                val kv = Base64.getDecoder().decode(inline)
                val receiver = DirectRtpReceiver(
                    listenPort = localPort,
                    masterKey = kv.copyOfRange(0, 16),
                    masterSalt = kv.copyOfRange(16, kv.size),
                ) { au -> _frames.tryEmit(au) }
                receiver.start()
                this@DirectRtpClient.receiver = receiver
                _state.value = State.STREAMING
                true
            }
        } catch (e: Exception) {
            _error.value = e.message ?: "connect failed"
            _state.value = State.ERROR
            false
        }
    }

    fun stop() {
        receiver?.close()
        receiver = null
        _state.value = State.IDLE
    }
}
