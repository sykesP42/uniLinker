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
    /** Called (rate-limited) when a sequence gap means loss — the client
     *  sends a PLI so the sender forces an IDR instead of waiting for the
     *  periodic GOP boundary (up to 2 s of artifacts). */
    private val onKeyFrameNeeded: (() -> Unit)? = null,
    /** NACK target (the sender's RTP bind port host:port from the answer).
     *  Null = RTX disabled (gaps go straight to the skip + PLI path). */
    private val nackTarget: java.net.InetSocketAddress? = null,
    /** Tile transport (PT 97): assembled tile frames flow here instead of
     *  the H264 AU path. Null = H264-only receiver. */
    private val onTileFrame: ((List<Pair<Int, ByteArray>>) -> Unit)? = null,
) : AutoCloseable {
    private var tileAssembler: TileAssembler? = null
    @Volatile var tileAssembled: Int = 0
        private set
    @Volatile var tileDropped: Int = 0
        private set
    @Volatile var tileOrphans: Int = 0
        private set
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val unprotector = SrtpUnprotector(masterKey, masterSalt)
    private val depacketizer = H264RtpDepacketizer()
    private val nalBuffer = ArrayList<ByteArray>()
    private var socket: DatagramSocket? = null
    private var loop: Job? = null
    private var lastPliMs = 0L

    // ── Reorder buffer + NACK (M5 RTX) ──
    // Single-threaded by construction (only the socket receive loop touches
    // these). In-order packets deliver immediately; a hole parks followers
    // in [pending], NACKs the range, and skips (with loss accounting + PLI)
    // after [recoveryTimeoutMs] without a fill. RTX copies carry their
    // ORIGINAL seq, so a fill simply advances expectedSeq through pending.
    private val pending = java.util.TreeMap<Int, ByteArray>()
    private var expectedSeq = -1
    private var holeSinceMs = 0L
    private var lastNackMs = 0L
    private var lastNackedEnd = -1
    /** Set when a hole was skipped MID-FRAME: everything in nalBuffer since
     *  the last marker belongs to a corrupt frame — drop it at the boundary
     *  instead of feeding the decoder garbage (PLI already requested). */
    @Volatile private var frameCorrupted = false
    // Assembly diagnostics (throttled log every ~2s)
    @Volatile var assembledAUs: Int = 0
        private set
    @Volatile var droppedCorruptAUs: Int = 0
        private set
    @Volatile var nalsBuffered: Int = 0
        private set
    // 250ms: must exceed the NACK→retransmit round trip. USB tether RTT is
    // ~0ms (fills land in ~2ms), but WiFi hotspot RTT measured ~130ms — a
    // 30ms window skipped EVERY hole over WiFi before the RTX fill landed,
    // so JPEG tiles always assembled truncated (black render). The window
    // is a timeout, not a delay: complete frames render as soon as filled.
    private val recoveryTimeoutMs: Long = 250L
    private val nackRetryMs: Long = 15L

    // ── Chaos injection (docs/spec/chaos-acceptance.md C1–C5) ──
    // Settable at runtime via adb (`--ei chaos_loss 5` deep-link extras);
    // 0 = off. Injection happens in handle() BEFORE any accounting so the
    // seq-gap detector sees injected loss exactly like real network loss.
    @Volatile var chaosLossPct: Int = 0
    @Volatile var chaosDupPct: Int = 0
    @Volatile var chaosLagMs: Int = 0
    private val chaosRandom = java.util.Random()

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

    /** Sliding loss window: (timestampMs, lost?) per packet, trimmed to 5 s.
     *  Guarded by [lossGate] — the RTP loop mutates it while the HUD poller
     *  iterates on the UI thread (ConcurrentModificationException crash). */
    private val lossWindow = ArrayDeque<Pair<Long, Boolean>>()
    private val lossGate = Any()

    /** (lost, total) within the last [windowMs] milliseconds. */
    fun lossWindowStats(windowMs: Long = 5000L): Pair<Int, Int> {
        val cutoff = System.currentTimeMillis() - windowMs
        synchronized(lossGate) {
            while (lossWindow.isNotEmpty() && lossWindow.first().first < cutoff) lossWindow.removeFirst()
            var lost = 0
            for ((_, wasLost) in lossWindow) if (wasLost) lost++
            return Pair(lost, lossWindow.size)
        }
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
                } catch (e: Exception) {
                    if (sock.isClosed || !isActive) break
                    // Swallowed exceptions here once silently froze the whole
                    // receive pipeline (pending-empty giveUp) — log them.
                    android.util.Log.e("DirectRtp", "receive loop error", e)
                }
            }
        }
    }

    private fun handle(packet: DatagramPacket) {
        val bytes = packet.data.copyOfRange(packet.offset, packet.offset + packet.length)
        if (bytes.size < 12) return
        // RTCP rides the same mux port (PT byte ≥ 64); video RTP = PT 96
        // (H264 stream) and PT 97 (tile transport) share the seq space.
        val pt = bytes[1].toInt() and 0x7F
        if (pt != 96 && pt != 97) return

        // Chaos injection (C1–C5 acceptance): drop BEFORE any accounting so
        // the reorder detector sees injected loss exactly like real loss.
        if (chaosLossPct > 0 && chaosRandom.nextInt(100) < chaosLossPct) return

        receivedPackets++

        val seq = ((bytes[2].toInt() and 0xFF) shl 8) or (bytes[3].toInt() and 0xFF)
        val now = System.currentTimeMillis()

        // ── Reorder + NACK pipeline (single-threaded, lazy timeout) ──
        if (expectedSeq == -1) {
            expectedSeq = seq
            deliver(bytes, now)
            return
        }
        val delta = (seq - expectedSeq + 65536) % 65536
        when {
            // The expected packet arrived (in-order or as an RTX fill) —
            // deliver it, advance the baseline, and flush followers. Without
            // the advance+drain the baseline pinned at the hole and RTX
            // resent it forever (NACK storm → dup resends → REPLAY_FAIL).
            delta == 0 -> {
                deliver(bytes, now)
                expectedSeq = (expectedSeq + 1) % 65536
                drainPending(now)
            }
            delta == 1 -> {
                expectedSeq = (expectedSeq + 1) % 65536
                deliver(bytes, now)
                drainPending(now)
            }
            delta < 32768 -> {
                // Forward jump: hole of (delta-1) packets. Park, NACK, and
                // wait briefly for RTX before skipping.
                pending[seq] = bytes
                if (holeSinceMs == 0L) {
                    holeSinceMs = now
                    lastNackedEnd = -1
                }
                sendNack(expectedSeq, seq - 1, now)
                drainPending(now)
                giveUpCheck(now)
            }
            // Backward delta: a late RTX for an already-skipped seq, or an
            // old duplicate — silently ignore.
        }
    }

    /** Deliver every consecutive pending packet starting at expectedSeq. */
    private fun drainPending(now: Long) {
        while (true) {
            val next = pending.remove(expectedSeq) ?: break
            deliver(next, now)
            expectedSeq = (expectedSeq + 1) % 65536
        }
        if (pending.isEmpty() && holeSinceMs != 0L) holeSinceMs = 0L
    }

    /** Give up on the current hole after [recoveryTimeoutMs]: account the
     *  skipped range as loss, jump to the next pending packet, and ask the
     *  encoder for an IDR (rate-limited). */
    private fun giveUpCheck(now: Long) {
        if (holeSinceMs == 0L || now - holeSinceMs <= recoveryTimeoutMs) return
        if (pending.isEmpty()) {
            // Hole with nothing parked: keep waiting (the RTX fill may still
            // land); the 5s loss-window trim bounds the accounting.
            holeSinceMs = 0L
            return
        }
        val nextPending = pending.firstKey()
        val skipped = (nextPending - expectedSeq + 65536) % 65536
        if (skipped > 0) {
            lostPackets += skipped
            frameCorrupted = true
            synchronized(lossGate) {
                repeat(skipped.coerceAtMost(400)) { lossWindow.add(Pair(now, true)) }
            }
            android.util.Log.w("DirectRtp", "gap $expectedSeq->${nextPending} unrecovered (lost $skipped, waited ${now - holeSinceMs}ms)")
            if (onKeyFrameNeeded != null && now - lastPliMs >= 500L) {
                lastPliMs = now
                try { onKeyFrameNeeded.invoke() } catch (_: Exception) { }
            }
        }
        expectedSeq = nextPending
        holeSinceMs = 0L
        drainPending(now)
    }

    /** Send a NACK for [fromSeq]..[toSeq] to the sender's RTP port, rate-
     *  limited to one datagram per [nackRetryMs] for the same range. */
    private fun sendNack(fromSeq: Int, toSeq: Int, now: Long) {
        val target = nackTarget ?: return
        if (fromSeq == toSeq + 1) return // empty range
        val sameRange = (toSeq - lastNackedEnd + 65536) % 65536 < 4
        if (sameRange && now - lastNackMs < nackRetryMs) return
        lastNackMs = now
        lastNackedEnd = toSeq
        val seqs = mutableListOf<Int>()
        var s = fromSeq
        while ((s - toSeq - 1 + 65536) % 65536 != 0 && seqs.size < 32) {
            seqs.add(s)
            s = (s + 1) % 65536
        }
        val buf = ByteArray(6 + seqs.size * 2)
        buf[0] = 0x55; buf[1] = 0x4E; buf[2] = 0x49; buf[3] = 0x4E // "UNIN"
        buf[4] = (seqs.size shr 8).toByte(); buf[5] = seqs.size.toByte()
        seqs.forEachIndexed { i, v ->
            buf[6 + i * 2] = (v shr 8).toByte()
            buf[7 + i * 2] = v.toByte()
        }
        android.util.Log.d("DirectRtp", "nack: $fromSeq..$toSeq (${seqs.size} seqs) -> $target")
        try {
            socket?.send(DatagramPacket(buf, buf.size, target.address, target.port))
        } catch (_: Exception) {
            // NACK delivery is best-effort; skip + PLI is the fallback.
        }
    }

    /** Delivery entry from the reorder pipeline: applies chaos dup/lag
     *  injection, then runs the serialized payload work. */
    private fun deliver(bytes: ByteArray, nowArrival: Long) {
        val lag = chaosLagMs
        if (lag > 0) {
            // C3: deliver late on a worker (parallel workers = reorder; the
            // payload gate keeps the shared state intact).
            scope.launch {
                kotlinx.coroutines.delay(lag.toLong())
                payloadWork(bytes, System.currentTimeMillis())
            }
            return
        }
        payloadWork(bytes, nowArrival)
        if (chaosDupPct > 0 && chaosRandom.nextInt(100) < chaosDupPct) {
            receivedPackets++
            payloadWork(bytes, nowArrival + 1)
        }
    }

    /** Unprotect + depacketize + HUD accounting, shared by the inline and
     *  chaos-lagged delivery paths. SERIALIZED: nalBuffer and the
     *  depacketizer assume a single consumer (lag deliveries arrive on
     *  parallel coroutine workers — concurrent mutation crashed both). */
    private val payloadGate = Any()
    private fun payloadWork(bytes: ByteArray, nowArrival: Long) = synchronized(payloadGate) {
        val clear = unprotector.unprotect(bytes)
        if (clear == null) {
            authFailures++
            frameCorrupted = true
            depacketizer.reset()
            android.util.Log.w(
                "DirectRtp",
                "auth failure #$authFailures (pkts=$receivedPackets len=${bytes.size})",
            )
            return@synchronized
        }
        // Shared accounting (jitter/bytes/loss-window) runs for BOTH payload
        // types — the tile branch once returned early and skipped it, making
        // the HUD report 100% loss on a healthy stream.
        // RFC-3550-style interarrival jitter, exponentially smoothed.
        if (lastArrivalMs > 0) {
            val delta = (nowArrival - lastArrivalMs).coerceAtLeast(0).toDouble()
            jitterMs += (kotlin.math.abs(delta - 33.3) - jitterMs) / 16.0
        }
        lastArrivalMs = nowArrival
        bytesReceived += bytes.size
        synchronized(lossGate) {
            lossWindow.add(Pair(nowArrival, false))
            val cutoff = nowArrival - 5000L
            while (lossWindow.isNotEmpty() && lossWindow.first().first < cutoff) lossWindow.removeFirst()
        }

        // Tile transport dispatch (PT 97): the assembled tile frame flows to
        // the renderer; corruption handling matches the H264 path.
        if ((clear[1].toInt() and 0x7F) == 97) {
            if (tileAssembler == null && onTileFrame != null) {
                tileAssembler = TileAssembler(onTileFrame)
            }
            val frameId = ((clear[4].toLong() and 0xFF) shl 24) or ((clear[5].toLong() and 0xFF) shl 16) or
                ((clear[6].toLong() and 0xFF) shl 8) or (clear[7].toLong() and 0xFF)
            val orphan = tileAssembler?.push(H264RtpDepacketizer.rtpPayload(clear), frameId) ?: false
            if (orphan) tileOrphans++
            tileAssembled = tileAssembler?.assembledFrames ?: 0
            tileDropped = tileAssembler?.droppedFrames ?: 0
            if (orphan) {
                // Predecessor chunk skipped — the frame is unrecoverable.
                frameCorrupted = true
                if (onKeyFrameNeeded != null && nowArrival - lastPliMs >= 500L) {
                    lastPliMs = nowArrival
                    try { onKeyFrameNeeded.invoke() } catch (_: Exception) { }
                }
            }
            return@synchronized
        }

        // RFC-3550-style interarrival jitter, exponentially smoothed.
        if (lastArrivalMs > 0) {
            val delta = (nowArrival - lastArrivalMs).coerceAtLeast(0).toDouble()
            jitterMs += (kotlin.math.abs(delta - 33.3) - jitterMs) / 16.0
        }
        lastArrivalMs = nowArrival
        bytesReceived += bytes.size

        val marker = (clear[1].toInt() and 0x80) != 0
        nalBuffer.addAll(depacketizer.push(H264RtpDepacketizer.rtpPayload(clear)))
        if (nalBuffer.size % 50 == 0 && nalBuffer.size > 0) nalsBuffered = nalBuffer.size
        synchronized(lossGate) {
            lossWindow.add(Pair(nowArrival, false))
            val cutoff = nowArrival - 5000L
            while (lossWindow.isNotEmpty() && lossWindow.first().first < cutoff) lossWindow.removeFirst()
        }

        if (marker && nalBuffer.isNotEmpty()) {
            val au = concat(nalBuffer)
            nalBuffer.clear()
            assembledAUs++
            if (frameCorrupted) {
                // A skipped packet landed inside this frame — feeding the
                // decoder garbage poisons references for the whole GOP and
                // reads as ~1fps mush. Freeze cleanly instead: drop the AU,
                // the PLI (already fired) rebuilds with the next IDR.
                frameCorrupted = false
                depacketizer.reset()
                droppedCorruptAUs++
                if (droppedCorruptAUs % 20 == 1) android.util.Log.w("DirectRtp", "dropped corrupt AU #$droppedCorruptAUs after mid-frame skip (${au.size}B)")
                return@synchronized
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
