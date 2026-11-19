package com.unilinker.android.mediatier

/**
 * Tile-frame assembler for the M6 tile transport (PT 97).
 *
 * Wire payload per RTP packet (after SRTP unprotect):
 * ```
 * [tileIdx u16][tileCount u16][chunkIdx u8][chunkCount u8][jpeg...][flags u8]
 * ```
 * flags bit0 = last packet of the tile frame. All packets of one frame share
 * the RTP timestamp (the frame id), and the receiver's reorder buffer
 * guarantees IN-ORDER delivery — so assembly is a simple append: chunks of a
 * tile arrive sequentially, and the frame's marker packet flushes it.
 *
 * Corruption rule — "latest frame wins, nothing partial renders": an orphan
 * chunk (its predecessor was skipped) or a missing tile at the marker means
 * the frame is discarded whole and a full refresh is requested via PLI.
 * Corruption never cascades — each frame is independent.
 */
class TileAssembler(private val onTileFrame: (List<Pair<Int, ByteArray>>) -> Unit) {

    private var frameId = -1L
    private var tileCount = 0
    private val tiles = HashMap<Int, ByteArray>() // tileIdx → accumulating jpeg
    private val order = ArrayList<Int>()          // tileIdx arrival order
    private var frameOrphan = false               // a chunk's predecessor was skipped

    var assembledFrames: Int = 0
        private set
    var droppedFrames: Int = 0
        private set
    var orphanFrames: Int = 0
        private set
    var lastFrameId: Long = -1
        private set

    /** Feed one unprotected tile payload. Returns false when the packet was
     *  an orphan (its predecessor was skipped) — the caller maps that to a
     *  full-refresh request. */
    fun push(payload: ByteArray, frameId: Long): Boolean {
        if (payload.size < 7) return true
        val tileIdx = ((payload[0].toInt() and 0xFF) shl 8) or (payload[1].toInt() and 0xFF)
        val tileCountF = ((payload[2].toInt() and 0xFF) shl 8) or (payload[3].toInt() and 0xFF)
        val chunkIdx = payload[4].toInt() and 0xFF
        val jpeg = payload.copyOfRange(6, payload.size - 1)
        val lastOfFrame = (payload[payload.size - 1].toInt() and 1) != 0

        if (frameId != this.frameId) {
            // New frame: any unflushed previous frame was superseded — drop it.
            if (tiles.isNotEmpty()) droppedFrames++
            reset(frameId)
        }

        var orphan = false
        if (chunkIdx == 0) {
            if (tiles.containsKey(tileIdx)) {
                // Duplicate chunk-0 (RTX double fill) — restart the tile.
                tiles[tileIdx] = jpeg
            } else {
                tiles[tileIdx] = jpeg
                order.add(tileIdx)
            }
        } else {
            val acc = tiles[tileIdx]
            if (acc == null) {
                orphan = true // predecessor skipped: the tile is unrecoverable
            } else {
                tiles[tileIdx] = acc + jpeg
            }
        }
        if (orphan) frameOrphan = true

        if (lastOfFrame) {
            // In-order delivery guarantees every SENT packet of this frame
            // has been delivered by the time the marker processes — only
            // changed tiles are sent, so tiles.size IS the frame's tile set.
            val complete = !frameOrphan && tiles.isNotEmpty()
            if (complete) {
                val result = order.map { it to (tiles[it] ?: ByteArray(0)) }
                assembledFrames++
                lastFrameId = frameId
                onTileFrame(result)
            } else {
                droppedFrames++
                if (frameOrphan) orphanFrames++
            }
            reset(-1)
        }
        return orphan
    }

    private fun reset(newFrameId: Long) {
        frameId = newFrameId
        tileCount = 0
        tiles.clear()
        order.clear()
        frameOrphan = false
    }
}
