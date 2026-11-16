package com.unilinker.android.mediatier

import org.jitsi.srtp.SrtpContextFactory
import org.jitsi.srtp.SrtpErrorStatus
import org.jitsi.srtp.SrtpPolicy
import org.jitsi.utils.ByteArrayBuffer
import org.jitsi.utils.logging2.LoggerImpl

/**
 * Unprotects incoming SDES-keyed SRTP packets (suite AES_CM_128_HMAC_SHA1_80,
 * RFC 3711). M3 direct-RTP tier (docs/spec/M3-direct-rtp.md): the Windows
 * sender announces its master key in the a=crypto line delivered over the
 * HTTP control plane; per-SSRC session keys follow the same KDF.
 *
 * Wraps jitsi-srtp (the pure-Java implementation battle-tested by Jitsi
 * Meet) — no hand-written crypto anywhere in this module.
 */
class SrtpUnprotector(masterKey: ByteArray, masterSalt: ByteArray) : AutoCloseable {
    // AES-CM 128 / HMAC-SHA1-80 — the WebRTC-default profile, same shape as
    // the sender's SrtpPolicy (SIPSorcery and jitsi-srtp share this lineage).
    private val srtpPolicy = SrtpPolicy(
        SrtpPolicy.AESCM_ENCRYPTION, 16,
        SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 10, 14,
    )
    private val srtcpPolicy = SrtpPolicy(
        SrtpPolicy.AESCM_ENCRYPTION, 16,
        SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 10, 14,
    )

    private val factory = SrtpContextFactory(
        /* sender = */ false, masterKey, masterSalt, srtpPolicy, srtcpPolicy,
        LoggerImpl(SrtpUnprotector::class.java.name),
    )
    private val contexts = HashMap<Int, org.jitsi.srtp.SrtpCryptoContext>()

    /**
     * Unprotect one SRTP packet in place.
     * @return the RTP packet bytes (header + plaintext payload), or null when
     *         authentication fails or the replay window rejects the packet.
     */
    fun unprotect(packet: ByteArray, offset: Int = 0, length: Int = packet.size): ByteArray? {
        val ssrc = extractSsrc(packet, offset)
        val context = contexts.getOrPut(ssrc) {
            factory.deriveContext(ssrc, /* initial roc = */ 0)
        }
        val bab = View(packet, offset, length)
        val status = context.reverseTransformPacket(bab, /* authTag present = */ false)
        if (status != SrtpErrorStatus.OK) return null
        // reverseTransform shrank the view: auth tag stripped, payload decrypted.
        return packet.copyOfRange(offset, offset + bab.length)
    }

    override fun close() {
        factory.close()
        contexts.clear()
    }

    companion object {
        /** RTP header: SSRC at bytes 8..11. */
        fun extractSsrc(packet: ByteArray, offset: Int = 0): Int =
            ((packet[offset + 8].toInt() and 0xFF) shl 24) or
                ((packet[offset + 9].toInt() and 0xFF) shl 16) or
                ((packet[offset + 10].toInt() and 0xFF) shl 8) or
                (packet[offset + 11].toInt() and 0xFF)
    }
}

/**
 * Minimal ByteArrayBuffer view over an existing array — jitsi-srtp's
 * transform APIs mutate the buffer in place and adjust the length (the SRTP
 * auth tag is stripped on unprotect). Pure data plumbing, no logic.
 */
private class View(
    private var buf: ByteArray,
    private var off: Int,
    private var len: Int,
) : ByteArrayBuffer {
    override fun getBuffer(): ByteArray = buf
    override fun getOffset(): Int = off
    override fun getLength(): Int = len
    override fun setLength(l: Int) {
        len = l
    }
    override fun setOffset(o: Int) {
        off = o
    }
    override fun isInvalid(): Boolean = off < 0 || len < 0 || off + len > buf.size
    override fun readRegionToBuff(regionOffset: Int, regionLength: Int, out: ByteArray) {
        System.arraycopy(buf, off + regionOffset, out, 0, regionLength)
    }
    override fun grow(size: Int) {
        buf = buf.copyOf(buf.size + size)
    }
    override fun append(bytes: ByteArray, appendLength: Int) {
        grow(appendLength)
        System.arraycopy(bytes, 0, buf, off + len, appendLength)
        len += appendLength
    }
    override fun shrink(delta: Int) {
        len -= delta
    }
}
