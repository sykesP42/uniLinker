package com.unilinker.android.mediatier

import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.os.Build
import java.io.File

/**
 * Device capability + utilization probe for the receiver panel.
 *
 * Static info (SoC / GPU / decoders / memory) is gathered once and cached;
 * usage (CPU% / GPU% / free memory) is sampled on demand at the panel's
 * 1 Hz diagnostics cadence. All sources are readable from an app uid on
 * this device: /proc/stat, /proc/meminfo and the Adreno kgsl sysfs nodes.
 */
object DeviceProbe {

    /** One utilization sample; deltas are vs the previous call. */
    data class Usage(val cpuPct: Int, val gpuPct: Int, val memAvailMb: Double, val decodeMs: Double?)

    /** The activity feeds the current per-frame decode cost (tile decode ms
     *  in tile mode; null in H264 mode where MediaCodec hides it). */
    @Volatile var decodeMsHint: Double? = null

    private var cachedStatic: List<Pair<String, String>>? = null
    private var prevCpu: Pair<Long, Long>? = null // (busy, total)
    private var prevGpu: Pair<Long, Long>? = null
    private var gpuName: String? = null

    /** SoC + GPU + decoder inventory. Cached after the first call. */
    fun staticInfo(): List<Pair<String, String>> {
        cachedStatic?.let { return it }
        val rows = mutableListOf<Pair<String, String>>()
        rows.add(Pair("机型", Build.MODEL))
        val cores = Runtime.getRuntime().availableProcessors()
        val maxFreqMhz = readLong("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq") / 1000
        rows.add(Pair("SoC", Build.HARDWARE.uppercase() + " · " + cores + "核 @" + maxFreqMhz + "MHz"))
        rows.add(Pair("GPU", gpuRendererName()))
        rows.add(Pair("系统", "Android " + Build.VERSION.RELEASE + " (API " + Build.VERSION.SDK_INT + ", " +
            (Build.SUPPORTED_ABIS.firstOrNull() ?: "?") + ")"))
        rows.add(Pair("内存", fmtGb(totalMemGb()) + " 总量 · " + fmtGb(memAvailKb() / 1024.0) + " 可用"))

        // Video decoder inventory: hardware (vendor OMX) vs software (google).
        try {
            val infos = MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos
            val decoders: List<MediaCodecInfo> = infos
                .filter { ci -> !ci.isEncoder }
                .filter { ci -> ci.supportedTypes.any { t -> t.startsWith("video/") } }
            val hw: List<String> = decoders
                .filter { ci -> !ci.name.startsWith("OMX.google") }
                .flatMap { ci -> ci.supportedTypes.map { t -> shortMime(t) } }
                .distinct()
            val sw: List<String> = decoders
                .filter { ci -> ci.name.startsWith("OMX.google") }
                .flatMap { ci -> ci.supportedTypes.map { t -> shortMime(t) } }
                .distinct()
            rows.add(Pair("硬解", hw.joinToString("/").ifEmpty { "无" }))
            rows.add(Pair("软解", sw.joinToString("/").ifEmpty { "无" }))
        } catch (e: Exception) {
            rows.add(Pair("硬解", "枚举失败: " + e.message))
        }
        cachedStatic = rows
        return rows
    }

    /** One utilization sample; deltas vs the previous call. Cheap file
     *  reads — safe at the diagnostics 1 Hz cadence. */
    fun usageSample(): Usage {
        // CPU: aggregate busy/(busy+idle) across all cores, delta per sample.
        var cpuPct = 0
        try {
            val line = File("/proc/stat").bufferedReader().use { it.readLine() }
            if (line.startsWith("cpu ")) {
                val parts = line.trim().split(Regex("\\s+")).drop(1).mapNotNull { v -> v.toLongOrNull() }
                if (parts.size >= 4) {
                    val idle = parts[3]
                    val total = parts.sum()
                    val p = prevCpu
                    prevCpu = Pair(total - idle, total)
                    if (p != null && total > p.second) {
                        val busyD = (total - idle) - p.first
                        val totD = total - p.second
                        if (totD > 0) cpuPct = (busyD * 100 / totD).toInt().coerceIn(0, 100)
                    }
                }
            }
        } catch (e: Exception) { }

        // GPU: kgsl gpubusy = "busy total" since boot — delta per sample.
        var gpuPct = 0
        try {
            val text = File("/sys/class/kgsl/kgsl-3d0/gpubusy").bufferedReader().use { it.readText().trim() }
            val parts = text.split(Regex("\\s+")).mapNotNull { v -> v.toLongOrNull() }
            if (parts.size >= 2) {
                val p = prevGpu
                prevGpu = Pair(parts[0], parts[1])
                if (p != null && parts[1] > p.second) {
                    val d = (parts[0] - p.first) * 100 / (parts[1] - p.second)
                    gpuPct = d.toInt().coerceIn(0, 100)
                }
            }
        } catch (e: Exception) { }

        return Usage(cpuPct, gpuPct, memAvailKb() / 1024.0, decodeMsHint)
    }

    private fun memAvailKb(): Long {
        return try {
            val line = File("/proc/meminfo").bufferedReader().use { r ->
                r.readLines().firstOrNull { l -> l.startsWith("MemAvailable") }
            }
            line?.split(Regex("\\s+"))?.getOrNull(1)?.toLongOrNull() ?: 0L
        } catch (e: Exception) {
            0L
        }
    }

    private fun totalMemGb(): Double {
        return try {
            val line = File("/proc/meminfo").bufferedReader().use { r ->
                r.readLines().firstOrNull { l -> l.startsWith("MemTotal") }
            }
            (line?.split(Regex("\\s+"))?.getOrNull(1)?.toLongOrNull() ?: 0L) / 1024.0 / 1024.0
        } catch (e: Exception) {
            0.0
        }
    }

    private fun fmtGb(gb: Double): String = java.lang.String.format("%.1f GB", gb)

    /** GPU renderer via SurfaceFlinger (one process spawn, cached). */
    private fun gpuRendererName(): String {
        gpuName?.let { return it }
        try {
            val p = Runtime.getRuntime().exec(arrayOf("dumpsys", "SurfaceFlinger"))
            val out = p.inputStream.bufferedReader().use { it.readText() }
            p.waitFor()
            val gles = out.lineSequence().firstOrNull { it.contains("GLES:", ignoreCase = true) } ?: ""
            val parts = gles.substringAfter("GLES:").split(",")
            val name = parts.getOrNull(1)?.trim() ?: ""
            gpuName = if (name.isNotEmpty()) name else "未知"
        } catch (e: Exception) {
            gpuName = "未知"
        }
        return gpuName ?: "未知"
    }

    private fun shortMime(mime: String): String = when (mime) {
        "video/avc" -> "AVC"
        "video/hevc" -> "HEVC"
        "video/x-vnd.on2.vp8" -> "VP8"
        "video/x-vnd.on2.vp9" -> "VP9"
        "video/mp4v-es" -> "MPEG4"
        "video/3gpp" -> "H263"
        "video/x-ms-wmv" -> "VC1"
        "video/divx" -> "DivX"
        "video/mpeg2" -> "MPEG2"
        else -> mime.substringAfter("video/").uppercase()
    }

    private fun readLong(path: String): Long = try {
        File(path).bufferedReader().use { it.readText().trim().toLongOrNull() } ?: 0L
    } catch (e: Exception) { 0L }
}
