namespace UniLinker.Core;

/// <summary>
/// H.264 SPS VUI bitstream-restriction fixup (M7 round 3, data-gated):
/// the SM-P605's Venus decoder (omx.qcom, API 27) echoes NONE of the
/// low-latency MediaFormat keys (probe verdict, logcat 2026-11-28) — the
/// vendor-key route is dead on this decoder. Moonlight's answer for old
/// Qualcomm decoders is the SPS-level lever: rewrite the VUI
/// bitstream_restriction block to max_num_reorder_frames=0 +
/// max_dec_frame_buffering=1, which the decoder must honor because it is
/// part of the stream itself. We never emit B-frames (bf=0), so reordering
/// depth 0 is semantically exact; buffering depth 1 matches our
/// one-reference-frame P chain.
///
/// Parse the SPS RBSP up to vui_parameters_present_flag, drop any existing
/// VUI, and append a minimal one carrying the restrictions. Everything
/// before the VUI is copied bit-exact.
/// </summary>
public static class H264SpsFixup
{
    /// <summary>Patch every SPS NAL found in an Annex-B buffer. Non-SPS
    /// NALs and unparseable SPSs pass through verbatim (a parse miss must
    /// never break the stream). Returns the ORIGINAL array when nothing
    /// matched, so the hot path (no SPS in a P-frame AU) costs one header
    /// scan and ZERO allocations — the earlier version rebuilt the whole
    /// AU into a List&lt;byte&gt; per frame even when unpatched (~1-3ms of
    /// encode-worker time per frame).</summary>
    public static byte[] PatchAnnexB(byte[] au)
    {
        if (!ContainsSps(au)) return au;
        var outList = new List<byte>(au.Length + 16);
        int pos = 0;
        bool patched = false;
        while (pos < au.Length)
        {
            int sc = FindStartCode(au, pos);
            if (sc < 0)
            {
                for (int k = pos; k < au.Length; k++) outList.Add(au[k]);
                break;
            }
            int scLen = au[sc] == 0 && au[sc + 1] == 0 && au[sc + 2] == 0 ? 4 : 3;
            int nalStart = sc + scLen;
            int next = FindStartCode(au, nalStart);
            int nalEnd = next < 0 ? au.Length : next;
            bool replaced = false;
            if (nalEnd - nalStart > 3 && (au[nalStart] & 0x1F) == 7)
            {
                var nal = new byte[nalEnd - nalStart];
                Buffer.BlockCopy(au, nalStart, nal, 0, nal.Length);
                var fixedSps = PatchSpsNal(nal);
                if (fixedSps != null)
                {
                    for (int k = 0; k < scLen; k++) outList.Add(au[sc + k]);
                    outList.AddRange(fixedSps);
                    replaced = true;
                    patched = true;
                }
            }
            if (!replaced)
            {
                for (int k = sc; k < nalEnd; k++) outList.Add(au[k]);
            }
            pos = nalEnd;
        }
        return patched ? outList.ToArray() : au;
    }

    /// <summary>Patch one SPS NAL (header byte included, no start code).
    /// Returns the patched NAL, or null when parsing fails.</summary>
    public static byte[]? PatchSpsNal(byte[] nal)
    {
        try
        {
            var rbsp = UnescapeRbsp(nal, 1); // skip the NAL header byte
            var r = new BitReader(rbsp);
            var w = new BitWriter();

            // ── seq_parameter_set_data — copy bit-exact up to the VUI flag ──
            uint profileIdc = r.ReadUI(8); w.WriteUI(profileIdc, 8);
            w.WriteUI(r.ReadUI(8), 8);      // constraint flags
            w.WriteUI(r.ReadUI(8), 8);      // level_idc
            if (profileIdc is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
            {
                uint chromaFormat = r.ReadUE(); w.WriteUE(chromaFormat);
                if (chromaFormat == 3) w.WriteUI(r.ReadUI(1), 1); // separate_colour_plane
                w.WriteUE(r.ReadUE());          // bit_depth_luma_minus8
                w.WriteUE(r.ReadUE());          // bit_depth_chroma_minus8
                w.WriteUE(r.ReadUE());          // qpprime_y_zero_transform_bypass
                uint scaling = r.ReadUI(1); w.WriteUI(scaling, 1);
                if (scaling == 1)
                {
                    for (int s = 0; s < 8; s++)
                    {
                        if (r.ReadUI(1) != 0) SkipScalingList(r, w, 16);
                        else w.WriteUI(0, 1);
                        if (s < 6) continue;
                        if (r.ReadUI(1) != 0) SkipScalingList(r, w, 64);
                        else w.WriteUI(0, 1);
                    }
                }
            }
            w.WriteUE(r.ReadUE());          // log2_max_frame_num_minus4
            uint pocType = r.ReadUE(); w.WriteUE(pocType);
            if (pocType == 0) w.WriteUE(r.ReadUE());  // log2_max_pic_order_cnt_lsb
            else if (pocType == 1)
            {
                w.WriteUI(r.ReadUI(1), 1);  // delta_pic_order_always_zero
                w.WriteUE(r.ReadUE());      // offset_for_non_ref_pic
                w.WriteUE(r.ReadUE());      // offset_for_top_to_bottom
                uint n = r.ReadUE(); w.WriteUE(n);
                for (uint k = 0; k < n; k++) w.WriteUE(r.ReadUE());
            }
            w.WriteUE(r.ReadUE());          // max_num_ref_frames
            w.WriteUI(r.ReadUI(1), 1);      // gaps_in_frame_num_value_allowed
            w.WriteUE(r.ReadUE());          // pic_width_in_mbs_minus1
            w.WriteUE(r.ReadUE());          // pic_height_in_map_units_minus1
            uint frameMbsOnly = r.ReadUI(1); w.WriteUI(frameMbsOnly, 1);
            if (frameMbsOnly == 0) w.WriteUI(r.ReadUI(1), 1); // mb_adaptive_frame_field
            w.WriteUI(r.ReadUI(1), 1);      // direct_8x8_inference
            uint cropping = r.ReadUI(1); w.WriteUI(cropping, 1);
            if (cropping == 1)
            {
                for (int c = 0; c < 4; c++) w.WriteUE(r.ReadUE());
            }

            // Everything before here is copied; an existing VUI is DROPPED
            // and replaced with the minimal restriction set. (Reading the
            // old vui flag leaves the old body + trailing bits unread —
            // intentional: we stop and rebuild from here.)
            _ = r.ReadUI(1); // vui_parameters_present_flag (discard)

            w.WriteUI(1, 1);                // vui_parameters_present_flag = 1
            w.WriteUI(0, 1);                // aspect_ratio_info_present = 0
            w.WriteUI(0, 1);                // overscan_info_present = 0
            w.WriteUI(0, 1);                // video_signal_type_present = 0
            w.WriteUI(0, 1);                // chroma_loc_info_present = 0
            w.WriteUI(0, 1);                // timing_info_present = 0
            w.WriteUI(0, 1);                // nal_hrd_parameters_present = 0
            w.WriteUI(0, 1);                // vcl_hrd_parameters_present = 0
            w.WriteUI(0, 1);                // pic_struct_present = 0
            w.WriteUI(1, 1);                // bitstream_restriction_present = 1
            w.WriteUI(0, 1);                // motion_vectors_over_pic_boundaries = 0
            w.WriteUE(2);                   // max_bytes_per_pic_denom
            w.WriteUE(1);                   // max_bits_per_mb_denom
            w.WriteUE(16);                  // log2_max_mv_length_horizontal
            w.WriteUE(16);                  // log2_max_mv_length_vertical
            w.WriteUE(0);                   // max_num_reorder_frames = 0 (bf=0)
            w.WriteUE(1);                   // max_dec_frame_buffering = 1
            w.WriteTrailingBits();

            // Re-wrap: NAL header (keep the original's forbidden-zero/NRI,
            // type=7) + re-escaped RBSP.
            var body = EscapeRbsp(w.ToBytes());
            var outNal = new byte[1 + body.Length];
            outNal[0] = nal[0];
            Buffer.BlockCopy(body, 0, outNal, 1, body.Length);
            return outNal;
        }
        catch
        {
            return null; // parse miss — the caller keeps the original NAL
        }
    }

    /// <summary>H.264 7.3.2.1.1.1 scaling_list parse-and-reemit. se(v)
    /// entries pass through as raw ue(v) indices (same bits in, same bits
    /// out — the ue index IS the se codeword's payload).</summary>
    private static void SkipScalingList(BitReader r, BitWriter w, int size)
    {
        uint last = 8, next = 8;
        for (int k = 0; k < size; k++)
        {
            if (next != 0)
            {
                uint deltaIdx = r.ReadUE(); w.WriteUE(deltaIdx);
                long delta = ((deltaIdx + 1) / 2) * ((deltaIdx & 1) == 1 ? 1 : -1);
                next = (uint)((last + delta + 256) % 256);
                if (next != 0) last = next;
            }
        }
    }

    /// <summary>Cheap presence check: does any NAL in this Annex-B stream
    /// have type 7 (SPS)? Walks start codes and reads one byte per NAL —
    /// no buffer copies.</summary>
    private static bool ContainsSps(byte[] au)
    {
        int i = 0;
        while (i < au.Length - 4)
        {
            if (au[i] != 0 || au[i + 1] != 0) { i++; continue; }
            int hdr;
            if (au[i + 2] == 1) hdr = i + 3;
            else if (au[i + 2] == 0 && au[i + 3] == 1) hdr = i + 4;
            else { i++; continue; }
            if (hdr < au.Length && (au[hdr] & 0x1F) == 7) return true;
            i = hdr;
        }
        return false;
    }

    private static int FindStartCode(byte[] b, int from)
    {
        for (int i = from; i < b.Length - 3; i++)
        {
            if (b[i] == 0 && b[i + 1] == 0)
            {
                if (b[i + 2] == 1) return i;
                if (b[i + 2] == 0 && b[i + 3] == 1) return i;
            }
        }
        return -1;
    }

    /// <summary>Strip emulation-prevention bytes (00 00 03 → 00 00) from a
    /// NAL payload starting at [start].</summary>
    internal static byte[] UnescapeRbsp(byte[] nal, int start)
    {
        var outList = new List<byte>(nal.Length - start);
        int zeros = 0;
        for (int i = start; i < nal.Length; i++)
        {
            if (zeros >= 2 && nal[i] == 3 && i + 1 < nal.Length && nal[i + 1] <= 3)
            {
                zeros = 0; continue; // drop the escape byte
            }
            zeros = nal[i] == 0 ? zeros + 1 : 0;
            outList.Add(nal[i]);
        }
        return outList.ToArray();
    }

    /// <summary>Re-insert emulation-prevention bytes.</summary>
    internal static byte[] EscapeRbsp(byte[] rbsp)
    {
        var outList = new List<byte>(rbsp.Length + 8);
        int zeros = 0;
        foreach (var b in rbsp)
        {
            if (zeros == 2 && b <= 3)
            {
                outList.Add(3);
                zeros = 0;
            }
            zeros = b == 0 ? zeros + 1 : 0;
            outList.Add(b);
        }
        return outList.ToArray();
    }

    /// <summary>MSB-first bit reader with Exp-Golomb support.</summary>
    internal struct BitReader
    {
        private readonly byte[] _buf;
        private int _pos;
        public BitReader(byte[] buf) { _buf = buf; _pos = 0; }
        public uint ReadUI(int bits)
        {
            uint v = 0;
            for (int i = 0; i < bits; i++)
            {
                int byteIdx = _pos >> 3;
                if (byteIdx >= _buf.Length) throw new IndexOutOfRangeException("SPS bitstream overrun");
                int bit = (_buf[byteIdx] >> (7 - (_pos & 7))) & 1;
                v = (v << 1) | (uint)bit;
                _pos++;
            }
            return v;
        }
        /// <summary>ue(v): the Exp-Golomb index — for se(v) fields the same
        /// index passes back through <see cref="H264SpsFixup.BitWriter.WriteUE"/>
        /// unchanged (codeword-identical passthrough).</summary>
        public uint ReadUE()
        {
            int zeros = 0;
            while (ReadUI(1) == 0)
            {
                zeros++;
                if (zeros > 31) throw new FormatException("SPS ue(v) overrun");
            }
            if (zeros == 0) return 0;
            return (1u << zeros) - 1 + ReadUI(zeros);
        }
    }

    /// <summary>MSB-first bit writer with Exp-Golomb support.</summary>
    internal sealed class BitWriter
    {
        private readonly List<byte> _buf = new(64);
        private int _bitPos; // bits written into the last byte so far
        public void WriteUI(uint v, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                int bit = (int)((v >> i) & 1);
                if (_bitPos == 0) _buf.Add(0);
                if (bit != 0) _buf[^1] |= (byte)(1 << (7 - _bitPos));
                _bitPos = (_bitPos + 1) & 7;
            }
        }
        public void WriteUE(uint v)
        {
            uint val = v + 1;
            int nBits = 0;
            uint t = val;
            while (t > 1) { t >>= 1; nBits++; }
            for (int i = 0; i < nBits; i++) WriteUI(0, 1);
            WriteUI(val, nBits + 1);
        }
        public void WriteTrailingBits()
        {
            WriteUI(1, 1);
            while (_bitPos != 0) WriteUI(0, 1);
        }
        public byte[] ToBytes() => _buf.ToArray();
    }
}
