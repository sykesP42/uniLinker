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

    @Volatile var authFailures: Int = 0
        private set

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

        // Loss detection: a sequence gap means the IDR-size packet bursts are
        // being dropped somewhere between sender socket and our socket.
        val seq = ((clear[2].toInt() and 0xFF) shl 8) or (clear[3].toInt() and 0xFF)
        if (lastSeq >= 0) {
            val gap = (seq - lastSeq - 1 + 65536) % 65536
            if (gap > 0) {
                android.util.Log.w("DirectRtp", "seq gap $lastSeq->$seq (lost $gap pkts)")
            }
        }
        lastSeq = seq

        val marker = (clear[1].toInt() and 0x80) != 0
        nalBuffer.addAll(depacketizer.push(H264RtpDepacketizer.rtpPayload(clear)))
        if (marker && nalBuffer.isNotEmpty()) {
            val au = concat(nalBuffer)
            nalBuffer.clear()
            if (receivedPackets % 150 < 3) {
                android.util.Log.i(
                    "DirectRtp",
                    "pkts=$receivedPackets au=${au.size}B",
                )
            }
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
