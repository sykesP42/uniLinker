package com.unilinker.android.mediatier

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.net.DatagramPacket
import java.net.DatagramSocket

/**
 * Direct-RTP receive loop (M3, docs/spec/M3-direct-rtp.md): binds a UDP port,
 * unprotects SRTP packets with the sender's announced key and reassembles
 * H.264 Annex-B access units. Frame boundary = RTP marker bit (the sender
 * marks the last packet of each AU).
 *
 * Decoding/rendering is the caller's job — [onAu] receives one complete
 * Annex-B access unit per video frame.
 */
class DirectRtpReceiver(
    private val listenPort: Int,
    masterKey: ByteArray,
    masterSalt: ByteArray,
    private val onAu: (ByteArray) -> Unit,
) : AutoCloseable {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val unprotector = SrtpUnprotector(masterKey, masterSalt)
    private val depacketizer = H264RtpDepacketizer()
    private val nalBuffer = ArrayList<ByteArray>()
    private var lastSeq = -1
    private var socket: DatagramSocket? = null
    private var loop: Job? = null

    /** Packets accepted (RTP video, unprotected OK). For diagnostics. */
    @Volatile var receivedPackets: Int = 0
        private set

    @Volatile var lostPackets: Int = 0
        private set

    @Volatile var authFailures: Int = 0
        private set

    @Volatile var bytesReceived: Long = 0
        private set

    /** RFC-3550-style interarrival jitter (ms), exponentially smoothed. */
    @Volatile var jitterMs: Double = 0.0
        private set
    private var lastArrivalMs: Long = -1

    /** Sliding loss window: (timestampMs, lost?) per packet, trimmed to 5 s. */
    private val lossWindow = ArrayDeque<Pair<Long, Boolean>>()

    /** (lost, total) within the last [windowMs] milliseconds. */
    fun lossWindowStats(windowMs: Long = 5000L): Pair<Int, Int> {
        val cutoff = System.currentTimeMillis() - windowMs
        while (lossWindow.isNotEmpty() && lossWindow.first().first < cutoff) lossWindow.removeFirst()
        var lost = 0
        for ((_, wasLost) in lossWindow) if (wasLost) lost++
        return Pair(lost, lossWindow.size)
    }

    fun start() {
        if (loop?.isActive == true) return
        val sock = DatagramSocket(null).apply {
            reuseAddress = true
            // The sender bursts whole IDR frames (hundreds of packets in
            // microseconds); the OS receive queue must hold a full IDR or the
            // burst is dropped before our loop can drain it.
            receiveBufferSize = 4 * 1024 * 1024
            bind(java.net.InetSocketAddress(listenPort))
        }
        socket = sock
        loop = scope.launch {
            val buf = ByteArray(65536)
            val packet = DatagramPacket(buf, buf.size)
            while (isActive && !sock.isClosed) {
                try {
                    sock.receive(packet)
                    handle(packet)
                } catch (_: java.net.SocketTimeoutException) {
                    // continue
                } catch (_: Exception) {
                    if (sock.isClosed || !isActive) break
                }
            }
        }
    }

    private fun handle(packet: DatagramPacket) {
        val bytes = packet.data.copyOfRange(packet.offset, packet.offset + packet.length)
        if (bytes.size < 12) return
        // RTCP rides the same mux port (PT byte ≥ 64); only video RTP here.
        if ((bytes[1].toInt() and 0x7F) != 96) return
        receivedPackets++

        // Sequence continuity FIRST (from the plaintext RTP header): gaps are
        // network loss — the packet never arrived, so record lost placeholders
        // in the sliding window BEFORE the current packet's entry.
        val seq = ((bytes[2].toInt() and 0xFF) shl 8) or (bytes[3].toInt() and 0xFF)
        val nowArrival = System.currentTimeMillis()
        if (lastSeq >= 0) {
            val gap = (seq - lastSeq - 1 + 65536) % 65536
            if (gap > 0) {
                lostPackets += gap
                repeat(gap.coerceAtMost(400)) { lossWindow.add(Pair(nowArrival, true)) }
                android.util.Log.w("DirectRtp", "seq gap $lastSeq->$seq (lost $gap pkts)")
            }
        }
        lastSeq = seq

        val clear = unprotector.unprotect(bytes)
        if (clear == null) {
            authFailures++
            depacketizer.reset()
            android.util.Log.w(
                "DirectRtp",
                "auth failure #$authFailures (pkts=$receivedPackets len=${bytes.size})",
            )
            return
        }

        // RFC-3550-style interarrival jitter, exponentially smoothed.
        val nowMs = System.currentTimeMillis()
        if (lastArrivalMs > 0) {
            val delta = (nowMs - lastArrivalMs).coerceAtLeast(0).toDouble()
            jitterMs += (kotlin.math.abs(delta - 33.3) - jitterMs) / 16.0
        }
        lastArrivalMs = nowMs
        bytesReceived += bytes.size

        val marker = (clear[1].toInt() and 0x80) != 0
        nalBuffer.addAll(depacketizer.push(H264RtpDepacketizer.rtpPayload(clear)))
        lossWindow.add(Pair(nowArrival, false))
        val cutoff = nowArrival - 5000L
        while (lossWindow.isNotEmpty() && lossWindow.first().first < cutoff) lossWindow.removeFirst()

        if (marker && nalBuffer.isNotEmpty()) {
            val au = concat(nalBuffer)
            nalBuffer.clear()
            if (au.isNotEmpty()) onAu(au)
        }
    }

    override fun close() {
        loop?.cancel()
        socket?.close()
        socket = null
        unprotector.close()
        scope.cancel()
    }

    private fun concat(parts: List<ByteArray>): ByteArray {
        val total = parts.sumOf { it.size }
        val out = ByteArray(total)
        var pos = 0
        for (p in parts) {
            System.arraycopy(p, 0, out, pos, p.size)
            pos += p.size
        }
        return out
    }
}
