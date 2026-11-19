package com.unilinker.android.mediatier

/**
 * Tile assembler for the M6 tile transport (PT 97) - PER-TILE incremental.
 *
 * Wire payload per RTP packet (after SRTP unprotect):
 * [tileIdx u16][tileCount u16][chunkIdx u8][chunkCount u8][jpeg...][flags]
 * where flags bit0 = FlagLastOfFrame (sender marks the frame's LAST packet).
 *
 * The receiver's reorder buffer guarantees IN-ORDER delivery, so each
 * tile's chunks append sequentially; a tile COMPLETES when the last chunk
 * (chunkIdx+1 == chunkCount) lands - it is delivered to the renderer
 * IMMEDIATELY without waiting for the rest of the frame. Unchanged tiles
 * are never sent, so the renderer's backbuffer accumulates updates - the
 * VNC incremental model. Corruption is bounded to one tile and self-heals
 * with the next full refresh.
 */
class TileAssembler(
    private val onTile: (tileIdx: Int, tileCount: Int, isLastOfFrame: Boolean, jpeg: ByteArray) -> Unit,
) {

    private val partial = HashMap<Int, ByteArray>() // tileIdx -> accumulating jpeg
    var assembledTiles: Int = 0
        private set
    var droppedTiles: Int = 0
        private set

    /** Feed one unprotected tile payload (RTP header stripped). */
    fun push(payload: ByteArray) {
        if (payload.size < 7) return
        val tileIdx = ((payload[0].toInt() and 0xFF) shl 8) or (payload[1].toInt() and 0xFF)
        val chunkIdx = payload[4].toInt() and 0xFF
        val chunkCount = payload[5].toInt() and 0xFF
        val isLastOfFrame = (payload[payload.size - 1].toInt() and 0x01) != 0
        val jpeg = payload.copyOfRange(6, payload.size - 1)

        if (chunkIdx == 0) {
            // Duplicate chunk-0 (RTX double fill) - restart the tile.
            partial[tileIdx] = jpeg
        } else {
            val acc = partial[tileIdx]
            if (acc == null) {
                // Predecessor skipped: the tile is unrecoverable this round -
                // it self-heals with the next full refresh.
                droppedTiles++
                return
            }
            partial[tileIdx] = acc + jpeg
        }

        if (chunkIdx + 1 == chunkCount) {
            val done = partial.remove(tileIdx)
            if (done != null) {
                assembledTiles++
                onTile(tileIdx, chunkCount, isLastOfFrame, done)
            }
        }
    }

    fun reset() {
        partial.clear()
    }
}
