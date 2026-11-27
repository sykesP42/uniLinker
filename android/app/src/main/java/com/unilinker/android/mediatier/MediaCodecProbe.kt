package com.unilinker.android.mediatier

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build

/**
 * MediaCodec low-latency key probe (M7 R3), ported from Moonlight's
 * MediaCodecHelper probe chain. The 2013-era MSM8974 decoder pipeline
 * costs ~100ms end-to-end; if the component honors a low-latency vendor
 * key that floor drops toward the modern ~20-30ms class. If nothing is
 * recognized, Tile mode (CPU JPEG decode, 2-4ms/tile) remains the only
 * low-latency route on this device — that is the roadmap gate.
 *
 * Recognition signal: vendor config params a component ACCEPTS are echoed
 * into its output format (the OMX/QTI vendor-extension contract), so
 * keys set before configure() and later present in the output format are
 * "recognized" — not merely silently ignored.
 */
object MediaCodecProbe {
    private val vendorKeys = listOf(
        "vendor.qti-ext-dec-low-latency.enable",   // Qualcomm (MSM8974 generation)
        "vendor.qti-ext-dec-picture-order.enable", // Qualcomm: decode-order output
        "low-latency",                             // Android 11 key (pre-R: ignored)
        "vendor.hisi-ext-low-latency-dec.enable",  // HiSilicon Kirin
        "vendor.mtk-vdec-lowlatancy.enable",       // MediaTek (sic — upstream typo)
        // Moonlight's MediaCodecHelper sets this on QCOM decoders to clamp
        // the pipeline depth to 1 frame - the probe now tests whether the
        // Venus decoder honors it (it was never probed in R3).
        "max-dec-frame-buffering",
    )

    @Volatile var probeResult: String = "not probed yet"
        private set

    /** Set every candidate on [format] before configure(). Returns the keys
     *  that were set (checked against the output format after start). */
    fun applyLowLatencyKeys(format: MediaFormat): List<String> {
        val set = mutableListOf<String>()
        if (Build.VERSION.SDK_INT >= 30) {
            format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            set += MediaFormat.KEY_LOW_LATENCY
        }
        for (key in vendorKeys) {
            try {
                format.setInteger(key, 1)
                set += key
            } catch (_: Exception) { /* very old MediaFormat impls may reject */ }
        }
        return set
    }

    /** Which of the [setKeys] the component echoed back — call after start
     *  and again on INFO_OUTPUT_FORMAT_CHANGED (the first format may lag). */
    fun reportRecognized(codec: MediaCodec, setKeys: List<String>) {
        if (setKeys.isEmpty()) return
        try {
            val out = codec.outputFormat
            val recognized = setKeys.filter { out.containsKey(it) }
            probeResult = if (recognized.isEmpty()) {
                "no low-latency key recognized (set: $setKeys) — Tile is the only low-latency path on ${Build.MODEL}"
            } else {
                "recognized: $recognized on ${Build.MODEL} (API ${Build.VERSION.SDK_INT})"
            }
            android.util.Log.w("DirectRtp", "MediaCodecProbe: $probeResult")
        } catch (_: Exception) { }
    }
}
