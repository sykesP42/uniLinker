package com.unilinker.android.mediatier

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.view.Surface

/**
 * Tile renderer (M6 tile transport) - PER-TILE incremental drawing.
 *
 * Each completed tile is decoded and blitted onto a persistent full-size
 * Bitmap backbuffer at its grid position, then presented via
 * Surface.lockHardwareCanvas (the earlier TextureView.lockCanvas path
 * posted buffers that never reached the display on this device/Compose
 * combo - 63fps rendered, screen black).
 *
 * No frame-waiting: the screen updates as each tile completes (the VNC
 * model). Unchanged tiles are never sent, so the backbuffer accumulates
 * incremental updates - there is no tiling/overlap artifact by design.
 *
 * Gate A instrumentation: per-tile decode time is logged (throttled) -
 * <=15ms/tile keeps JPEG; >30ms/tile switches to LZ4 lossless.
 */
class TileRenderer(private val surfaceTexture: android.graphics.SurfaceTexture, private val dumpDir: String? = null) : AutoCloseable {
    private val surface = Surface(surfaceTexture)

    private var backbuffer: Bitmap? = null
    private var bufferSizedFor = 0 to 0
    private val decodeOptions = BitmapFactory.Options()

    // Decode throughput: 60fps × ~12 tiles/frame = 720 tiles/s; at ~5ms per
    // JPEG decode a single thread tops out at ~200 tiles/s — the measured
    // 60fps wall (2026-11-21). Tiles are independent grid cells, so decode +
    // blit parallelize freely; only present() must be serial. 3 workers keep
    // one core for the UI thread on this 4-core MSM8974.
    private val decodePool = java.util.concurrent.ThreadPoolExecutor(
        3, 3, 30L, java.util.concurrent.TimeUnit.SECONDS,
        java.util.concurrent.ArrayBlockingQueue(24),
    ) { r -> Thread(r, "tile-decode").apply { priority = Thread.MAX_PRIORITY } }.apply {
        // Queue full = decode can't keep up: drop the OLDEST queued tile
        // (its grid cell self-heals with the next frame that touches it —
        // the VNC model). Never drop the task being submitted.
        rejectedExecutionHandler = java.util.concurrent.ThreadPoolExecutor.DiscardOldestPolicy()
    }
    private val backbufferGate = Any()
    @Volatile private var closed = false

    // Gate A instrumentation
    var renderedTiles: Int = 0
        private set
    /** Frame-rate metric for the HUD: incremented on each frame-final tile
     *  (one per sender frame — tiles/frame varies with screen changes). */
    var presentedFrames: Int = 0
        private set
    var decodeFailures: Int = 0
        private set
    var lastDecodeMs: Double = 0.0
        private set

    private var logSeq = 0
    private var lastPresentNs = 0L
    private var presentPending = false

    /** Draw ONE completed tile: decode JPEG, blit at its grid rect, present.
     *  [gridCols]/[frameW]/[frameH] come from the negotiated tier geometry.
     *
     *  Present throttling: a full-frame hardware-canvas blit costs ~1-3ms on
     *  this 2013-era GPU; up to ~10 tiles/frame at 30fps would mean 300
     *  presents/s (unaffordable). Middle tiles coalesce to ≤1 present per
     *  12ms (dirty-pending), and the frame's LAST tile always presents
     *  promptly — latency stays bounded by the tile stream itself. */
    fun renderTile(tileIdx: Int, tileCount: Int, isLastOfFrame: Boolean, gridCols: Int, frameW: Int, frameH: Int, jpeg: ByteArray) {
        try {
            decodePool.execute { drawTile(tileIdx, tileCount, isLastOfFrame, gridCols, frameW, frameH, jpeg) }
        } catch (_: java.util.concurrent.RejectedExecutionException) {
            // Pool already shut down (session teardown) — drop the tile.
        }
    }

    /** Decode + blit ONE tile onto the backbuffer, then maybe present.
     *  Runs on the decode pool (3 workers): decode is the throughput wall
     *  and tiles are independent grid cells, so this parallelizes freely.
     *  Backbuffer/present state is serialized by [backbufferGate]. */
    private fun drawTile(tileIdx: Int, tileCount: Int, isLastOfFrame: Boolean, gridCols: Int, frameW: Int, frameH: Int, jpeg: ByteArray) {
        if (closed) return
        val start = System.nanoTime()
        val gridRows = (frameH + 199) / 200 // tile height 200
        val tileW = frameW / gridCols
        val tx = tileIdx % gridCols
        val ty = tileIdx / gridCols
        val tw = if (tx == gridCols - 1) frameW - tx * tileW else tileW
        val th = if (ty == gridRows - 1) frameH - ty * 200 else 200

        // Decode OUTSIDE the gate — this is the parallel part.
        val bmp = BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size, decodeOptions)
        if (bmp == null) {
            synchronized(backbufferGate) {
                decodeFailures++
                if (++logSeq % 20 == 1) android.util.Log.w("DirectRtp", "tile decode FAIL idx=$tileIdx bytes=${jpeg.size}")
            }
            return
        }

        synchronized(backbufferGate) {
            if (backbuffer == null || backbuffer!!.width != frameW || backbuffer!!.height != frameH) {
                val old = backbuffer
                backbuffer = Bitmap.createBitmap(frameW, frameH, Bitmap.Config.ARGB_8888)
                old?.recycle()
                // The TextureView's SurfaceTexture defaults its buffer to the
                // VIEW size (2560x1600 on this panel); an unscaled 1280x800
                // blit then covers only the top-left quadrant and the rest of
                // the displayed texture is stale buffer garbage (reads as the
                // "tiled mosaic" artifact). Pin the buffer to the video size —
                // 1:1 blits, TextureView upscales cleanly; MediaCodec adapts
                // to the same buffer size for the H264 path.
                if (bufferSizedFor != frameW to frameH) {
                    try { surfaceTexture.setDefaultBufferSize(frameW, frameH) } catch (_: Exception) { }
                    bufferSizedFor = frameW to frameH
                }
            }
            try {
                val rect = android.graphics.Rect(tx * tileW, ty * 200, tx * tileW + tw, ty * 200 + th)
                val canvas = android.graphics.Canvas(backbuffer!!)
                canvas.drawBitmap(bmp, null, rect, null)
            } finally {
                bmp.recycle()
            }

            val now = System.nanoTime()
            val sincePresentMs = (now - lastPresentNs) / 1e6
            if (isLastOfFrame || sincePresentMs >= 12.0) {
                present()
                lastPresentNs = now
                presentPending = false
                if (isLastOfFrame) presentedFrames++
            } else {
                presentPending = true
            }

            renderedTiles++
            lastDecodeMs = (System.nanoTime() - start) / 1e6
            if (renderedTiles % 120 == 1) {
                android.util.Log.d("DirectRtp",
                    "tile: rendered=$renderedTiles idx=$tileIdx/$tileCount last=$isLastOfFrame decode=${"%.1f".format(lastDecodeMs)}ms fails=$decodeFailures")
            }
            // DEBUG dumps: tile content as received (isolates assembly corruption
            // from draw bugs) + the composed backbuffer.
            if (dumpDir != null && renderedTiles % 150 == 1) {
                try {
                    java.io.FileOutputStream("$dumpDir/tile_n${renderedTiles}_i${tileIdx}.jpg").use { it.write(jpeg) }
                    val out = java.io.FileOutputStream("$dumpDir/backbuffer_n${renderedTiles}.png")
                    backbuffer!!.compress(Bitmap.CompressFormat.PNG, 60, out)
                    out.close()
                } catch (_: Exception) { }
            }
        }
    }

    /** Callers hold [backbufferGate]: serial hardware-canvas presents. */
    private fun present() {
        val canvas = surface.lockHardwareCanvas()
        try {
            if (logSeq < 3) {
                logSeq++
                android.util.Log.w("DirectRtp",
                    "present: canvas=${canvas.width}x${canvas.height} backbuffer=${backbuffer!!.width}x${backbuffer!!.height}")
            }
            // Scale-fill: correct even if the buffer size didn't take (e.g.,
            // a resize race) — the backbuffer always covers the full canvas.
            canvas.drawBitmap(backbuffer!!, null,
                android.graphics.Rect(0, 0, canvas.width, canvas.height), null)
        } finally {
            surface.unlockCanvasAndPost(canvas)
        }
    }

    override fun close() {
        decodePool.shutdownNow()
        synchronized(backbufferGate) {
            closed = true
            backbuffer?.recycle()
            backbuffer = null
        }
        // Release the canvas producer slot on the shared SurfaceTexture (the
        // TextureView owns the texture itself; only this wrapper goes away).
        try { surface.release() } catch (_: Exception) { }
    }
}
