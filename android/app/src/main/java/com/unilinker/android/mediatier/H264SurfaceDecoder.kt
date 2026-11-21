package com.unilinker.android.mediatier

import android.media.MediaCodec
import android.media.MediaFormat
import android.view.Surface
import java.util.concurrent.ConcurrentLinkedQueue
import kotlinx.coroutines.flow.MutableStateFlow

/**
 * Hardware H.264 decoder feeding a Surface - ASYNC MediaCodec mode.
 *
 * Why async: the previous sync drain loop polled dequeueOutputBuffer with a
 * 50ms timeout - a decoded frame waited up to 50ms (avg ~25ms) before being
 * rendered, pure added latency on every frame. The async callback fires the
 * moment a frame is ready: releaseOutputBuffer(index, true) renders
 * immediately - zero poll delay on the critical path.
 *
 * Input feeding: pending AUs queue up; onInputBufferAvailable hands out
 * codec input buffers, which are filled on the spot (a free-list bridges
 * buffers that arrive before data). submitAnnexB is safe from any thread.
 *
 * v7a devices have H.264 hardware decode across the board (the decode side
 * is not gated on encode, unlike libwebrtc's H264 receive path).
 */
class H264SurfaceDecoder(surface: Surface) : AutoCloseable {
    private val codec: MediaCodec
    private val pendingAUs = ConcurrentLinkedQueue<Pair<ByteArray, Long>>()
    private val freeInputs = ConcurrentLinkedQueue<Int>()
    private val codecGate = Any() // queueInputBuffer is not thread-safe across callers

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

        codec.setCallback(object : MediaCodec.Callback() {
            override fun onInputBufferAvailable(mc: MediaCodec, index: Int) {
                freeInputs.offer(index)
                feedPending()
            }

            override fun onOutputBufferAvailable(mc: MediaCodec, index: Int, outInfo: MediaCodec.BufferInfo) {
                // Render IMMEDIATELY - this is the latency-critical moment.
                mc.releaseOutputBuffer(index, true)
                decodedFrames++
            }

            override fun onOutputFormatChanged(mc: MediaCodec, f: MediaFormat) {
                MediaCodecProbe.reportRecognized(mc, probeKeys)
                val w = f.getInteger(MediaFormat.KEY_WIDTH)
                val h = f.getInteger(MediaFormat.KEY_HEIGHT)
                // Crop rect carries the REAL video size on some decoders.
                streamSize.value = if (f.containsKey("crop-left") && f.containsKey("crop-right")) {
                    Pair(
                        f.getInteger("crop-right") - f.getInteger("crop-left") + 1,
                        f.getInteger("crop-bottom") - f.getInteger("crop-top") + 1,
                    )
                } else {
                    Pair(w, h)
                }
            }

            override fun onError(mc: MediaCodec, e: MediaCodec.CodecException) {
                android.util.Log.e("DirectRtp", "codec error: ${e.message}")
            }
        })

        codec.configure(format, surface, null, 0)
        codec.start()
    }

    /** Enqueue one Annex-B access unit. Safe from any thread. */
    fun submitAnnexB(au: ByteArray, timestampUs: Long) {
        pendingAUs.offer(Pair(au, timestampUs))
        feedPending()
    }

    /** Pair queued AUs with free input buffers. Locked: onInputBufferAvailable
     *  (framework thread) and submitAnnexB (collector thread) both feed. */
    private fun feedPending() {
        synchronized(codecGate) {
            while (true) {
                val index = freeInputs.poll() ?: return
                val au = pendingAUs.poll() ?: run {
                    freeInputs.offer(index) // no data yet - keep the buffer
                    return
                }
                val input = codec.getInputBuffer(index) ?: return
                input.clear()
                input.put(au.first)
                codec.queueInputBuffer(index, 0, au.first.size, au.second, 0)
            }
        }
    }

    override fun close() {
        try {
            codec.stop()
        } catch (_: Exception) {
        }
        codec.release()
        pendingAUs.clear()
        freeInputs.clear()
    }
}
