package com.unilinker.android.mediatier

/**
 * H.264 RTP depacketizer (RFC 6184): RTP payloads in → Annex-B NAL units out.
 *
 * Supports the packetization modes the Windows sender emits:
 *  - single NAL unit (type 1..23): the whole payload is one NAL
 *  - STAP-A (type 24): several [2-byte size + NAL] in one packet
 *  - FU-A (type 28): one NAL split into [indicator + FU header + chunk]
 *    packets; reassembled with S/E bits. A new NAL with S=1 implicitly ends
 *    the previous incomplete one (dropped fragment) — matches how decoders
 *    recover from loss.
 *
 * The receiver hands the Annex-B bytes to MediaCodec; start codes are
 * re-inserted here (0x00000001) since MediaCodec's Annex-B mode expects them.
 */
class H264RtpDepacketizer {
    /** NAL bytes collected so far for the FU-A in progress (without start code). */
    private val fuBuffer = ArrayList<ByteArray>()
    private var fuIndicator = 0

    /** Feed one UNPROTECTED RTP payload; returns the complete NAL units it
     *  yields (0..n), each already prefixed with a 4-byte Annex-B start code. */
    fun push(payload: ByteArray, offset: Int = 0, length: Int = payload.size): List<ByteArray> {
        if (length == 0) return emptyList()
        val nalHeader = payload[offset].toInt() and 0xFF
        val nalType = nalHeader and 0x1F
        return when (nalType) {
            in 1..23 -> listOf(nalWithStartCode(payload, offset, length))
            24 -> parseStapA(payload, offset + 1, length - 1)
            28 -> parseFuA(
                indicator = nalHeader,
                fuHeader = payload[offset + 1].toInt() and 0xFF,
                chunk = payload,
                chunkOffset = offset + 2,
                chunkLength = length - 2,
            )
            else -> emptyList() // 25 MTAP16 / 26 MTAP24 / 29 FU-B: not emitted by our sender
        }
    }

    /** Drop any FU-A fragment mid-reassembly (e.g. after a decode error). */
    fun reset() {
        fuBuffer.clear()
        fuIndicator = 0
    }

    private fun parseFuA(indicator: Int, fuHeader: Int, chunk: ByteArray, chunkOffset: Int, chunkLength: Int): List<ByteArray> {
        val start = (fuHeader and 0x80) != 0
        val end = (fuHeader and 0x40) != 0
        val nalType = fuHeader and 0x1F
        if (start) {
            // Reconstruct the NAL header: keep F/NRI from the indicator,
            // take the real NAL type from the FU header.
            fuIndicator = (indicator and 0xE0) or nalType
            fuBuffer.clear()
        } else if (fuBuffer.isEmpty()) {
            return emptyList() // continuation without a start: loss, drop
        }
        fuBuffer.add(chunk.copyOfRange(chunkOffset, chunkOffset + chunkLength))
        if (!end) return emptyList()

        // Chunks hold the NAL payload WITHOUT the 1-byte NAL header; the
        // header is reconstructed from indicator+FU-type, so allocate 1+total.
        val total = fuBuffer.sumOf { it.size }
        val nal = ByteArray(1 + total)
        nal[0] = fuIndicator.toByte()
        var pos = 1
        for (part in fuBuffer) {
            System.arraycopy(part, 0, nal, pos, part.size)
            pos += part.size
        }
        fuBuffer.clear()
        return listOf(nalWithStartCode(nal, 0, nal.size))
    }

    private fun parseStapA(payload: ByteArray, offset: Int, length: Int): List<ByteArray> {
        val out = ArrayList<ByteArray>()
        var i = offset
        val end = offset + length
        while (i + 2 <= end) {
            val size = ((payload[i].toInt() and 0xFF) shl 8) or (payload[i + 1].toInt() and 0xFF)
            i += 2
            if (size == 0 || i + size > end) break // malformed entry: stop
            out.add(nalWithStartCode(payload, i, size))
            i += size
        }
        return out
    }

    companion object {
        const val START_CODE_LENGTH = 4

        /** Strip the RTP header (fixed 12B + CSRCs + optional extension) and
         *  return just the payload. [packet] must be a full RTP packet. */
        fun rtpPayload(packet: ByteArray): ByteArray {
            val cc = packet[0].toInt() and 0x0F
            var headerLen = 12 + cc * 4
            if ((packet[0].toInt() and 0x10) != 0) { // extension bit
                val extWords =
                    ((packet[headerLen + 2].toInt() and 0xFF) shl 8) or
                        (packet[headerLen + 3].toInt() and 0xFF)
                headerLen += 4 + extWords * 4
            }
            return packet.copyOfRange(headerLen, packet.size)
        }

        fun nalWithStartCode(src: ByteArray, offset: Int, length: Int): ByteArray {
            val out = ByteArray(START_CODE_LENGTH + length)
            out[0] = 0
            out[1] = 0
            out[2] = 0
            out[3] = 1
            System.arraycopy(src, offset, out, START_CODE_LENGTH, length)
            return out
        }
    }
}
