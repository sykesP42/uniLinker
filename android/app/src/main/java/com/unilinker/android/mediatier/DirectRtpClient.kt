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
import java.security.MessageDigest
import java.security.cert.X509Certificate
import java.util.Base64
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier
import java.security.cert.CertificateException
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

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

    private val baseHttp = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    private val http: OkHttpClient
        get() {
            val fp = pinnedFingerprint
            return if (fp != null) baseHttp.forTrust(PinnedTrustManager(fp)) else baseHttp
        }

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

    val receiverJitter: Double
        get() = receiver?.jitterMs ?: 0.0

    val receiverLossWindow: Pair<Int, Int>?
        get() = receiver?.lossWindowStats()

    /** SHA-256 fingerprint of a DER-encoded certificate, uppercase hex. */
    private fun fingerprintOf(cert: X509Certificate): String =
        MessageDigest.getInstance("SHA-256").digest(cert.encoded)
            .joinToString("") { "%02X".format(it) }

    /** Trust manager that accepts everything — used ONLY for the pairing
     *  probe; the trust decision is made out-of-band by the human comparing
     *  the SAS code, and every later connection is fingerprint-pinned. */
    private class ProbeTrustManager : X509TrustManager {
        @Volatile var lastFingerprint: String? = null
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            if (chain.isEmpty()) throw javax.net.ssl.SSLPeerUnverifiedException("empty chain")
            lastFingerprint = MessageDigest.getInstance("SHA-256").digest(chain[0].encoded)
                .joinToString("") { "%02X".format(it) }
        }
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
        override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
    }

    /** Trust manager that REQUIRES the pinned fingerprint. */
    private class PinnedTrustManager(private val pinned: String) : X509TrustManager {
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            if (chain.isEmpty()) throw javax.net.ssl.SSLPeerUnverifiedException("empty chain")
            val fp = MessageDigest.getInstance("SHA-256").digest(chain[0].encoded)
                .joinToString("") { "%02X".format(it) }
            if (!fp.equals(pinned, ignoreCase = true)) {
                throw CertificateException("fingerprint mismatch: $fp")
            }
        }
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
        override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
    }

    private fun OkHttpClient.forTrust(tm: X509TrustManager): OkHttpClient {
        val sc = SSLContext.getInstance("TLS")
        sc.init(null, arrayOf(tm), java.security.SecureRandom())
        return newBuilder()
            .sslSocketFactory(sc.socketFactory, tm)
            .hostnameVerifier(HostnameVerifier { _, _ -> true }) // fingerprint auth replaces hostname auth
            .build()
    }

    /**
     * Pairing: probe the device over TLS with a permissive trust manager,
     * report the fingerprint + SAS code (the human compares against the
     * sender's on-screen code), then pin the fingerprint for this session.
     * Returns the pairing code, or null when the probe fails.
     */
    suspend fun probePairing(): Pair<String, String>? = withContext(Dispatchers.IO) {
        android.util.Log.d("DirectRtp", "probe: entering withContext")
        val probeTm = ProbeTrustManager()
        val probeClient = http.forTrust(probeTm)
        try {
            val request = Request.Builder()
                .url("https://$host:$signalingPort/ulp/pairing")
                .build()
            android.util.Log.d("DirectRtp", "probe: executing request")
            probeClient.newCall(request).execute().use { response ->
                android.util.Log.d("DirectRtp", "probe: response ${response.code}")
                if (!response.isSuccessful) return@withContext null
                val obj = JSONObject(response.body!!.string())
                val fp = probeTm.lastFingerprint ?: return@withContext null
                Pair(obj.optString("pairingCode", ""), fp)
            }
        } catch (e: Throwable) {
            android.util.Log.e("DirectRtp", "pairing probe failed", e)
            _error.value = "pairing probe failed: ${e.javaClass.simpleName}: ${e.message}"
            null
        }
    }

    /** Persist the pinned fingerprint (call after the human confirms SAS). */
    fun pinFingerprint(fp: String) {
        pinnedFingerprint = fp
    }

    @Volatile var pinnedFingerprint: String? = null

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
                .url("https://$host:$signalingPort/signaling")
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
