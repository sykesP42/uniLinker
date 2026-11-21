package com.unilinker.android.mediatier

import android.media.MediaCodec
import android.media.MediaFormat
import android.view.Surface
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch

/**
 * Hardware H.264 decoder feeding a Surface (MediaCodec async-free variant).
 * [submitAnnexB] enqueues one complete Annex-B access unit; a drain loop
 * renders decoded frames to the surface. v7a devices have H.264 hardware
 * decode across the board (the decode side is not gated on encode, unlike
 * libwebrtc's H264 receive path).
 */
class H264SurfaceDecoder(surface: Surface) : AutoCloseable {
    private val codec: MediaCodec
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var drainJob: Job? = null

    /** Actual decoded stream size (from INFO_OUTPUT_FORMAT_CHANGED) — the UI
     *  uses it to aspect-fit the SurfaceView. Null until the first format. */
    val streamSize = MutableStateFlow<Pair<Int, Int>?>(null)

    @Volatile var decodedFrames: Int = 0
        private set

    /** R3 probe: keys set pre-configure, checked against the output format. */
    private val probeKeys: List<String>

    init {
        val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, 1280, 720)
            .apply {
                // Receiver-side sizing: the encoder adapts resolution; these are
                // just the initial hints. MediaCodec rescales to the stream.
                setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 512 * 1024)
            }
        probeKeys = MediaCodecProbe.applyLowLatencyKeys(format)
        codec = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        codec.configure(format, surface, null, 0)
        codec.start()
        MediaCodecProbe.reportRecognized(codec, probeKeys)
        drainJob = scope.launch { drainLoop() }
    }

    /** Enqueue one Annex-B access unit. Safe from any thread. */
    fun submitAnnexB(au: ByteArray, timestampUs: Long) {
        val index = codec.dequeueInputBuffer(10_000)
        if (index < 0) return // decoder backpressured: drop, next IDR will recover
        val input = codec.getInputBuffer(index) ?: return
        input.clear()
        input.put(au)
        codec.queueInputBuffer(index, 0, au.size, timestampUs, 0)
    }

    private suspend fun drainLoop() {
        val info = MediaCodec.BufferInfo()
        while (scope.isActive) {
            val out = codec.dequeueOutputBuffer(info, 50_000)
            when {
                out >= 0 -> {
                    codec.releaseOutputBuffer(out, true) // render to surface
                    decodedFrames++
                }
                out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    val f = codec.outputFormat
                    MediaCodecProbe.reportRecognized(codec, probeKeys) // echo may arrive with the first real format
                    val w = f.getInteger(MediaFormat.KEY_WIDTH)
                    val h = f.getInteger(MediaFormat.KEY_HEIGHT)
                    // Crop rect carries the REAL video size on some decoders.
                    if (f.containsKey("crop-left") && f.containsKey("crop-right")) {
                        val cw = f.getInteger("crop-right") - f.getInteger("crop-left") + 1
                        val ch = f.getInteger("crop-bottom") - f.getInteger("crop-top") + 1
                        streamSize.value = Pair(cw, ch)
                    } else {
                        streamSize.value = Pair(w, h)
                    }
                }
                else -> { /* try again later */ }
            }
        }
    }

    override fun close() {
        drainJob?.cancel()
        scope.cancel()
        try {
            codec.stop()
        } catch (_: Exception) {
        }
        codec.release()
    }
}
