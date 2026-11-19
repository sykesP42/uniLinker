package com.unilinker.android.mediatier

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.view.Surface
import android.view.TextureView

/**
 * Tile-frame renderer (M6 tile transport): draws decoded tiles onto the
 * TextureView's canvas over a persistent full-size Bitmap backbuffer.
 *
 * Replaces the MediaCodec pipeline in tile mode — CPU JPEG decode
 * (BitmapFactory + inBitmap reuse) + GPU canvas upload bypasses the
 * 2013-era MediaCodec latency floor (~100ms) that dominates the H264 path.
 *
 * Gate A instrumentation: per-frame decode time is logged (throttled) —
 * ≤15ms/tile keeps JPEG; >30ms/tile switches the design to LZ4 lossless.
 */
class TileRenderer(surfaceTexture: android.graphics.SurfaceTexture, private val dumpDir: String? = null) : AutoCloseable {
    // Present via the SurfaceTexture's buffer queue DIRECTLY. The earlier
    // TextureView.lockCanvas path posted buffers that never reached the
    // display on this device/Compose combo (63fps rendered, screen black) —
    // Surface.lockHardwareCanvas is the reliable route.
    private val surface = android.view.Surface(surfaceTexture)

    private var backbuffer: Bitmap? = null
    private val decodeOptions = BitmapFactory.Options()

    // Gate A instrumentation
    var lastFrameDecodeMs: Double = 0.0
        private set
    var lastTileCount: Int = 0
        private set
    var renderedFrames: Int = 0
        private set
    var decodeFailures: Int = 0
        private set

    private var testPhase = 0
    private var lastRenderMs = 0L

    /** DEBUG: animated test pattern — proves the lockCanvas presentation
     *  path independent of tile content. Runs when no tiles arrive for
     *  500ms (call from the collect loop's idle branch). */
    fun renderTestPattern(frameW: Int, frameH: Int) {
        val now = System.currentTimeMillis()
        if (now - lastRenderMs < 100) return
        lastRenderMs = now
        if (backbuffer == null || backbuffer!!.width != frameW || backbuffer!!.height != frameH) {
            backbuffer?.recycle()
            backbuffer = Bitmap.createBitmap(frameW, frameH, Bitmap.Config.ARGB_8888)
        }
        val canvas = android.graphics.Canvas(backbuffer!!)
        testPhase++
        val hue = (testPhase * 8) % 360
        canvas.drawColor(android.graphics.Color.HSVToColor(floatArrayOf(hue.toFloat(), 0.7f, 0.9f)))
        val paint = android.graphics.Paint().apply {
            color = android.graphics.Color.WHITE
            textSize = 80f
            isAntiAlias = true
        }
        canvas.drawText("TILE-TEST $testPhase", 100f, frameH / 2.0f, paint)
        present()
        renderedFrames++
    }

    private fun present() {
        val canvas = surface.lockHardwareCanvas()
        try {
            canvas.drawBitmap(backbuffer!!, 0f, 0f, null)
        } finally {
            surface.unlockCanvasAndPost(canvas)
        }
    }

    /** Draw one assembled tile frame. [tiles] = (tileIdx, jpeg bytes). */
    fun renderTileFrame(frameId: Long, gridCols: Int, gridRows: Int, frameW: Int, frameH: Int, tiles: List<Pair<Int, ByteArray>>) {
        val start = System.nanoTime()

        if (backbuffer == null || backbuffer!!.width != frameW || backbuffer!!.height != frameH) {
            backbuffer?.recycle()
            backbuffer = Bitmap.createBitmap(frameW, frameH, Bitmap.Config.ARGB_8888)
        }
        val canvas = android.graphics.Canvas(backbuffer!!)
        var failures = 0
        for ((tileIdx, jpeg) in tiles) {
            val tx = tileIdx % gridCols
            val ty = tileIdx / gridCols
            val tw = if (tx == gridCols - 1) frameW - tx * (frameW / gridCols) else frameW / gridCols
            val th = if (ty == gridRows - 1) frameH - ty * (frameH / gridRows) else frameH / gridRows
            val bmp = BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size, decodeOptions)
            if (bmp == null) {
                failures++
                continue
            }
            canvas.drawBitmap(bmp, null, android.graphics.Rect(tx * (frameW / gridCols), ty * (frameH / gridRows), tx * (frameW / gridCols) + tw, ty * (frameH / gridRows) + th), null)
            bmp.recycle()
        }
        val decodeMs = (System.nanoTime() - start) / 1e6

        present()

        renderedFrames++
        lastTileCount = tiles.size
        lastFrameDecodeMs = decodeMs
        // DEBUG: dump received tile JPEGs periodically — isolates assembly
        // corruption (tile content wrong here) from draw bugs.
        if (dumpDir != null && renderedFrames % 40 == 1) {
            try {
                tiles.take(3).forEachIndexed { i, (tileIdx, jpeg) ->
                    java.io.FileOutputStream("$dumpDir/tile_r${renderedFrames}_t${tileIdx}.jpg")
                        .use { it.write(jpeg) }
                }
            } catch (_: Exception) { }
        }
        if (failures > 0) decodeFailures += failures
        if (renderedFrames % 60 == 1) {
            android.util.Log.d("DirectRtp",
                "tile: frame=$frameId tiles=${tiles.size} decode=${"%.1f".format(decodeMs)}ms (${("%.1f".format(decodeMs / tiles.size))}ms/tile) failures=$failures")
        }
    }

    override fun close() {
        backbuffer?.recycle()
        backbuffer = null
    }
}
