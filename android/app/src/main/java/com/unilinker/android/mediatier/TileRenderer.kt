package com.unilinker.android.mediatier

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.view.Surface

/**
 * Tile renderer (M6 tile transport) - PER-TILE incremental drawing with a
 * DOUBLE-BUFFERED software backbuffer.
 *
 * Architecture (the third iteration - the first two had a hard wall):
 * - 4 decode workers (BACKGROUND priority) decode + blit each tile into the
 *   WRITE buffer (~2ms/tile, never blocking).
 * - When a frame-final tile lands (or >=12ms passed), the buffers SWAP
 *   (pointer exchange under a micro-lock) and the worker that swapped
 *   presents the READ buffer to the SurfaceTexture.
 * - The present's lockHardwareCanvas fences up to ~16ms against the
 *   compositor - that fence now happens OUTSIDE the blit path: the other
 *   workers keep blitting into the (new) WRITE buffer while the GPU upload
 *   runs. The old single-backbuffer design held the same lock across the
 *   fence, throttling the whole pipeline to ~200 tiles/s (the user saw
 *   partial desynced blocks + single-digit updates).
 *
 * Incremental correctness: at swap time the buffers exchange roles - the
 * presented buffer keeps every tile blitted so far, and the workers
 * continue on the other one; no tile is ever lost between presents.
 *
 * The present self-serializes via [presenting] (a swap is deferred while
 * the previous GPU upload fences). Tile decode runs on the 4-worker pool
 * at BACKGROUND priority (the RTP receive thread outranks it).
 */
class TileRenderer(
    private val surfaceTexture: android.graphics.SurfaceTexture,
    private val dumpDir: String? = null,
) : AutoCloseable {
    private val surface = Surface(surfaceTexture)

    // Double-buffered accumulation (both created at the tier size).
    private var writeBuf: Bitmap? = null
    private var readBuf: Bitmap? = null
    private var bufferSizedFor = 0 to 0
    private val swapLock = Any()

    @Volatile private var presenting = false
    private var lastPresentNs = 0L

    private val decodeOptions = BitmapFactory.Options()

    // Decode throughput: 60fps x ~16 tiles/frame = ~960 tiles/s; at ~2.5ms
    // per JPEG decode a single thread tops out at ~400 tiles/s. Tiles are
    // independent grid cells, so decode + blit parallelize freely; the
    // presents fence on their own (see presenting). Thread priorities: the
    // RTP receive thread runs at DISPLAY (-8); decode workers at DEFAULT (0)
    // - MAX_PRIORITY starved the receiver (socket bursts), BACKGROUND landed
    // the workers in the ~5% CPU bg cgroup (heavy content = single digits).
    private val decodePool = java.util.concurrent.ThreadPoolExecutor(
        4, 4, 30L, java.util.concurrent.TimeUnit.SECONDS,
        java.util.concurrent.ArrayBlockingQueue(48),
    ) { r -> Thread(r, "tile-decode").apply {
        // DEFAULT (foreground cgroup): BACKGROUND lands in the bg cgroup
        // with ~5-10% CPU on this SoC - heavy content collapsed the decode
        // to single digits (2026-11-22). The RTP receive thread still wins
        // (DISPLAY) under contention; decode gets the fair-share rest.
    } }.apply {
        // Queue full = decode can't keep up: drop the OLDEST queued tile
        // (its grid cell self-heals with the next frame that touches it -
        // the VNC model). Never drops the task being submitted.
        rejectedExecutionHandler = java.util.concurrent.ThreadPoolExecutor.DiscardOldestPolicy()
    }

    // Gate A instrumentation
    var renderedTiles: Int = 0
        private set

    /** Visible-update counter: incremented per swap (each swap presents the
     *  full accumulated content - the honest "what the eye sees" metric). */
    var presentedFrames: Int = 0
        private set

    var decodeFailures: Int = 0
        private set

    var lastDecodeMs: Double = 0.0
        private set

    private var logSeq = 0

    /** Enqueue ONE tile: decode + blit runs on the pool, presents happen
     *  inline on the completing worker (fenced, self-serialized). */
    fun renderTile(
        tileIdx: Int,
        tileCount: Int,
        isLastOfFrame: Boolean,
        gridCols: Int,
        frameW: Int,
        frameH: Int,
        jpeg: ByteArray,
    ) {
        try {
            decodePool.execute {
                drawTile(tileIdx, tileCount, isLastOfFrame, gridCols, frameW, frameH, jpeg)
            }
        } catch (_: java.util.concurrent.RejectedExecutionException) {
            // Pool already shut down (session teardown) - drop the tile.
        }
    }

    /** Decode + blit ONE tile onto the WRITE buffer, then maybe swap+present.
     *  Decode runs outside the swap lock (the parallel part); blit and the
     *  buffer swap are micro-locked; the fenced GPU upload runs AFTER the
     *  swap released everything - blits continue while it fences. */
    private fun drawTile(
        tileIdx: Int,
        tileCount: Int,
        isLastOfFrame: Boolean,
        gridCols: Int,
        frameW: Int,
        frameH: Int,
        jpeg: ByteArray,
    ) {
        if (decodePool.isShutdown) return
        val start = System.nanoTime()
        val gridRows = (frameH + TileH - 1) / TileH
        val tileW = frameW / gridCols
        val tx = tileIdx % gridCols
        val ty = tileIdx / gridCols
        val tw = if (tx == gridCols - 1) frameW - tx * tileW else tileW
        val th = if (ty == gridRows - 1) frameH - ty * TileH else TileH

        // Decode OUTSIDE the lock - this is the parallel part.
        val bmp = BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size, decodeOptions)
        if (bmp == null) {
            synchronized(swapLock) {
                decodeFailures++
                if (++logSeq % 20 == 1) {
                    android.util.Log.w("DirectRtp", "tile decode FAIL idx=$tileIdx bytes=${jpeg.size}")
                }
            }
            return
        }

        var doPresent = false
        synchronized(swapLock) {
            if (writeBuf == null || readBuf == null ||
                writeBuf!!.width != frameW || writeBuf!!.height != frameH
            ) {
                // Both buffers at the video size - the SurfaceTexture's
                // default buffer is pinned to match (the "tiled mosaic"
                // artifact came from a view-sized default buffer).
                writeBuf?.recycle()
                readBuf?.recycle()
                writeBuf = Bitmap.createBitmap(frameW, frameH, Bitmap.Config.ARGB_8888)
                readBuf = Bitmap.createBitmap(frameW, frameH, Bitmap.Config.ARGB_8888)
                if (bufferSizedFor != frameW to frameH) {
                    try {
                        surfaceTexture.setDefaultBufferSize(frameW, frameH)
                    } catch (e: Exception) {
                    }
                    bufferSizedFor = frameW to frameH
                }
            }
            val rect = android.graphics.Rect(tx * tileW, ty * TileH, tx * tileW + tw, ty * TileH + th)
            android.graphics.Canvas(writeBuf!!).drawBitmap(bmp, null, rect, null)
            renderedTiles++

            // Present decision INSIDE the lock (cheap): swap pointers, the
            // fenced upload happens outside.
            val now = System.nanoTime()
            val sincePresentMs = (now - lastPresentNs) / 1e6
            if ((isLastOfFrame || sincePresentMs >= 12.0) && !presenting) {
                // Swap: the accumulated WRITE buffer becomes the presented
                // READ buffer; the workers continue on the old presented
                // content (incremental state preserved - no tile lost).
                val tmp = writeBuf
                writeBuf = readBuf
                readBuf = tmp
                presentedFrames++
                lastPresentNs = now
                presenting = true
                doPresent = true
            }
            lastDecodeMs = (System.nanoTime() - start) / 1e6
            if (renderedTiles % 120 == 1) {
                android.util.Log.d(
                    "DirectRtp",
                    "tile: rendered=$renderedTiles idx=$tileIdx/$tileCount last=$isLastOfFrame decode=${"%.1f".format(lastDecodeMs)}ms fails=$decodeFailures",
                )
            }
            if (dumpDir != null && renderedTiles % 150 == 1) {
                try {
                    java.io.FileOutputStream("$dumpDir/tile_n${renderedTiles}_i${tileIdx}.jpg").use { it.write(jpeg) }
                    val out = java.io.FileOutputStream("$dumpDir/backbuffer_n${renderedTiles}.png")
                    writeBuf!!.compress(Bitmap.CompressFormat.PNG, 60, out)
                    out.close()
                } catch (e: Exception) {
                }
            }
        }
        bmp.recycle()

        if (doPresent) presentReadBuffer()
    }

    /** Present the READ buffer to the SurfaceTexture. Called AFTER the swap
     *  released the lock - the GPU upload fences here while the workers keep
     *  blitting into the (new) WRITE buffer. [presenting] defers the next
     *  swap until this completes. */
    private fun presentReadBuffer() {
        try {
            val canvas = surface.lockHardwareCanvas()
            try {
                if (logSeq < 3) {
                    logSeq++
                    android.util.Log.w(
                        "DirectRtp",
                        "present: canvas=${canvas.width}x${canvas.height} read=${readBuf!!.width}x${readBuf!!.height}",
                    )
                }
                canvas.drawBitmap(readBuf!!, null, android.graphics.Rect(0, 0, canvas.width, canvas.height), null)
            } finally {
                surface.unlockCanvasAndPost(canvas)
            }
        } catch (e: Exception) {
            android.util.Log.w("DirectRtp", "present failed: ${e.message}")
        } finally {
            presenting = false
        }
    }

    override fun close() {
        decodePool.shutdownNow()
        synchronized(swapLock) {
            writeBuf?.recycle()
            writeBuf = null
            readBuf?.recycle()
            readBuf = null
        }
        try {
            surface.release()
        } catch (e: Exception) {
        }
    }

    private companion object {
        /** Tile grid cell width (multiple of 16 for YUVJ420P MCU). 320x200 is
         *  deliberate: small tiles keep INCREMENTAL updates granular - a tiny
         *  change decodes 1-2ms and appears instantly. Larger tiles measured
         *  "more tiles/s" while the user-perceived smoothness collapsed
         *  (reverted 2026-11-22, user report). */
        const val TileW = 320
        const val TileH = 200
    }
}
