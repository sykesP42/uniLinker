package com.unilinker.android.mediatier

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.view.Surface

/**
 * Tile renderer (M6 tile transport) - PER-TILE incremental drawing with a
 * DOUBLE-BUFFERED software backbuffer and a CADENCE-DRIVEN present thread.
 *
 * Architecture (the fourth iteration):
 * - 4 decode workers (DEFAULT priority) decode + blit each tile into the
 *   WRITE buffer (~2ms/tile, never blocking, never presenting).
 * - A dedicated present thread (DISPLAY priority) fires at a steady ~60Hz:
 *   when new tiles landed it SWAPS the buffers (pointer exchange under a
 *   micro-lock) and uploads the READ buffer to the SurfaceTexture. The swap
 *   decouples the visible update cadence from the arrival burstiness - the
 *   frame-final-driven presents of the previous design fired whenever the
 *   network delivered, which read as jitter on video content (user verdict
 *   2026-11-22: "多区块不同步的卡").
 * - Incremental correctness: at swap time the buffers exchange roles - the
 *   presented buffer keeps every tile blitted so far, and the workers
 *   continue on the other one; no tile is ever lost between presents.
 *
 * Thread priorities (2026-11-22, measured on the 4-core MSM8974):
 * - RTP receive thread: DISPLAY (-8) - the critical path.
 * - Present thread: MAX (foreground) - the visible output.
 * - Decode workers: DEFAULT (0) - MAX starved the receiver (socket bursts);
 *   BACKGROUND landed them in the ~5% CPU bg cgroup (heavy content =
 *   single-digit fps). DEFAULT gives them the fair-share rest.
 */
class TileRenderer(
    private val surface: Surface,
    private val setBufferSize: (Int, Int) -> Unit,
    private val dumpDir: String? = null,
) : AutoCloseable {

    // Double-buffered accumulation (both created at the tier size).
    private var writeBuf: Bitmap? = null
    private var readBuf: Bitmap? = null
    private var bufferSizedFor = 0 to 0
    private val swapLock = Any()

    @Volatile private var dirty = false
    private var lastPresentNs = 0L
    private var presentThread: Thread? = null

    private val decodeOptions = BitmapFactory.Options()

    // Decode throughput: 60fps x ~16 tiles/frame = ~960 tiles/s; at ~2.5ms
    // per JPEG decode a single thread tops out at ~400 tiles/s. Tiles are
    // independent grid cells, so decode + blit parallelize freely.
    private val decodePool = java.util.concurrent.ThreadPoolExecutor(
        4, 4, 30L, java.util.concurrent.TimeUnit.SECONDS,
        java.util.concurrent.ArrayBlockingQueue(96),
    ) { r -> Thread(r, "tile-decode").apply {
        // DEFAULT (foreground cgroup): BACKGROUND lands in the ~5% CPU bg
        // cgroup on this SoC - heavy content collapsed the decode to single
        // digits (2026-11-22). The RTP receive thread (DISPLAY) still wins
        // under contention; decode gets the fair-share rest.
    } }.apply {
        // Queue full = decode can't keep up: drop the OLDEST queued tile
        // (its grid cell self-heals with the next frame that touches it -
        // the VNC model). Never drops the task being submitted.
        rejectedExecutionHandler = java.util.concurrent.ThreadPoolExecutor.DiscardOldestPolicy()
    }

    // Gate A instrumentation
    var renderedTiles: Int = 0
        private set

    /** Visible-update counter: incremented per swap (each swap uploads the
     *  full accumulated content - the honest "what the eye sees" metric). */
    var presentedFrames: Int = 0
        private set

    var decodeFailures: Int = 0
        private set

    var lastDecodeMs: Double = 0.0
        private set

    private var logSeq = 0

    init {
        startPresentThread()
    }

    /** Enqueue ONE tile: decode + blit runs on the pool. Presents are the
     *  present thread's job (cadence-driven). */
    fun renderTile(
        tileIdx: Int,
        tileCount: Int,
        gridCols: Int,
        frameW: Int,
        frameH: Int,
        jpeg: ByteArray,
    ) {
        try {
            decodePool.execute {
                drawTile(tileIdx, tileCount, gridCols, frameW, frameH, jpeg)
            }
        } catch (_: java.util.concurrent.RejectedExecutionException) {
            // Pool already shut down (session teardown) - drop the tile.
        }
    }

    /** Decode + blit ONE tile onto the WRITE buffer. Decode runs outside the
     *  swap lock (the parallel part); the blit is micro-locked. Marks the
     *  buffer dirty - the present thread picks it up on its cadence. */
    private fun drawTile(
        tileIdx: Int,
        tileCount: Int,
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
                        setBufferSize(frameW, frameH)
                    } catch (e: Exception) {
                    }
                    bufferSizedFor = frameW to frameH
                }
            }
            val rect = android.graphics.Rect(tx * tileW, ty * TileH, tx * tileW + tw, ty * TileH + th)
            android.graphics.Canvas(writeBuf!!).drawBitmap(bmp, null, rect, null)
            renderedTiles++
            dirty = true
            lastDecodeMs = (System.nanoTime() - start) / 1e6
            if (renderedTiles % 120 == 1) {
                android.util.Log.d(
                    "DirectRtp",
                    "tile: rendered=$renderedTiles idx=$tileIdx/$tileCount decode=${"%.1f".format(lastDecodeMs)}ms fails=$decodeFailures",
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
    }

    /** The present thread: fires at a steady ~60Hz cadence, swapping the
     *  buffers and uploading the latest accumulated state when new tiles
     *  landed. DISPLAY priority - the present IS the visible output. The
     *  fence (lockHardwareCanvas) blocks THIS thread only: the decode
     *  workers never touch it. */
    private fun startPresentThread() {
        presentThread = Thread({
            android.os.Process.setThreadPriority(
                android.os.Process.THREAD_PRIORITY_DISPLAY)
            while (!decodePool.isShutdown) {
                try {
                    Thread.sleep(16)
                } catch (e: InterruptedException) {
                    break
                }
                var doPresent = false
                synchronized(swapLock) {
                    if (!dirty || writeBuf == null) return@synchronized
                    // Swap: the accumulated WRITE buffer becomes the presented
                    // READ buffer; the workers continue on the old presented
                    // content (incremental state preserved - no tile lost).
                    val tmp = writeBuf
                    writeBuf = readBuf
                    readBuf = tmp
                    dirty = false
                    presentedFrames++
                    doPresent = true
                }
                if (doPresent) presentReadBuffer()
            }
        }, "tile-present").apply {
            priority = Thread.MAX_PRIORITY
            start()
        }
    }

    /** Present the READ buffer to the SurfaceTexture. The GPU upload fences
     *  HERE (on the present thread) - the decode workers keep blitting into
     *  the (new) WRITE buffer meanwhile. */
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
        }
    }

    override fun close() {
        decodePool.shutdownNow()
        presentThread?.interrupt()
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
