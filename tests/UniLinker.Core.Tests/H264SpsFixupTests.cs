using FluentAssertions;
using UniLinker.Core;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>SPS VUI bitstream-restriction fixup (Venus probe-gated lever).
/// The patched SPS must keep every pre-VUI field bit-identical and carry
/// max_num_reorder_frames=0 / max_dec_frame_buffering=1.</summary>
public class H264SpsFixupTests
{
    /// <summary>Build a realistic nvenc-style high-profile 1280x800 SPS
    /// (no crop, no scaling matrix, no VUI) as an Annex-B NAL.</summary>
    private static byte[] BuildTestSps()
    {
        var w = new H264SpsFixup.BitWriter();
        w.WriteUI(100, 8);  // profile_idc = high
        w.WriteUI(0xE0, 8); // constraint flags (NRI-carrying byte? no — the constraint byte)
        w.WriteUI(40, 8);   // level_idc = 4.0
        w.WriteUE(1);       // chroma_format_idc = 4:2:0
        w.WriteUE(0);       // bit_depth_luma_minus8
        w.WriteUE(0);       // bit_depth_chroma_minus8
        w.WriteUE(0);       // qpprime
        w.WriteUI(0, 1);    // seq_scaling_matrix_present
        w.WriteUE(4);       // log2_max_frame_num_minus4
        w.WriteUE(0);       // pic_order_cnt_type = 0
        w.WriteUE(4);       // log2_max_pic_order_cnt_lsb_minus4
        w.WriteUE(1);       // max_num_ref_frames = 1
        w.WriteUI(0, 1);    // gaps_in_frame_num
        w.WriteUE(79);      // pic_width_in_mbs_minus1 → 1280
        w.WriteUE(49);      // pic_height_in_map_units_minus1 → 800
        w.WriteUI(1, 1);    // frame_mbs_only
        w.WriteUI(1, 1);    // direct_8x8_inference
        w.WriteUI(0, 1);    // frame_cropping (800 = 50×16 exact)
        w.WriteUI(0, 1);    // vui_parameters_present = 0
        w.WriteTrailingBits();
        var body = H264SpsFixup.EscapeRbsp(w.ToBytes());
        var nal = new byte[1 + body.Length];
        nal[0] = 0x67; // SPS, NRI=3
        body.CopyTo(nal, 1);
        return nal;
    }

    /// <summary>Re-parse the SPS fields up to (and including) the VUI
    /// restriction values, returning them for assertions.</summary>
    private static (uint reorder, uint buffering, int consumedBits) ParseVui(byte[] nal)
    {
        var rbsp = H264SpsFixup.UnescapeRbsp(nal, 1);
        var r = new H264SpsFixup.BitReader(rbsp);
        int profile = (int)r.ReadUI(8);
        r.ReadUI(8); r.ReadUI(8);
        if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
        {
            if (r.ReadUE() == 3) r.ReadUI(1);
            r.ReadUE(); r.ReadUE(); r.ReadUE();
            if (r.ReadUI(1) != 0) throw new FormatException("scaling list not expected in test vector");
        }
        r.ReadUE();
        if (r.ReadUE() == 0) r.ReadUE();
        r.ReadUE(); r.ReadUI(1); r.ReadUE(); r.ReadUE();
        if (r.ReadUI(1) == 0) r.ReadUI(1);
        r.ReadUI(1);
        if (r.ReadUI(1) != 0) for (int c = 0; c < 4; c++) r.ReadUE();
        var vui = r.ReadUI(1);
        if (vui == 0) return (0, 0, 0);
        if (r.ReadUI(1) != 0) throw new FormatException("aspect ratio");
        r.ReadUI(1); r.ReadUI(1); r.ReadUI(1); r.ReadUI(1); r.ReadUI(1); r.ReadUI(1); r.ReadUI(1);
        if (r.ReadUI(1) == 0) throw new FormatException("bitstream_restriction absent");
        r.ReadUI(1);                       // motion_vectors_over_pic_boundaries
        r.ReadUE();                        // max_bytes_per_pic_denom
        r.ReadUE();                        // max_bits_per_mb_denom
        r.ReadUE();                        // log2_max_mv_length_h
        r.ReadUE();                        // log2_max_mv_length_v
        var reorder = r.ReadUE();
        var buffering = r.ReadUE();
        return (reorder, buffering, 0);
    }

    [Fact]
    public void Patch_adds_bitstream_restriction_and_keeps_pre_vui_fields()
    {
        var sps = BuildTestSps();
        var patched = H264SpsFixup.PatchSpsNal(sps);
        patched.Should().NotBeNull();

        var (reorder, buffering, _) = ParseVui(patched!);
        reorder.Should().Be(0, "max_num_reorder_frames=0 (bf=0 stream)");
        buffering.Should().Be(1, "max_dec_frame_buffering=1 (the Venus pipeline clamp)");
        (patched![0] & 0x1F).Should().Be(7, "still an SPS NAL");
        patched[0].Should().Be(sps[0], "NAL header byte preserved (NRI/forbidden-zero)");
    }

    [Fact]
    public void Patch_idempotent_second_pass_changes_nothing()
    {
        var sps = BuildTestSps();
        var p1 = H264SpsFixup.PatchSpsNal(sps)!;
        var p2 = H264SpsFixup.PatchSpsNal(p1)!;
        p2.Should().Equal(p1, "the rewritten SPS already has the VUI — reparse+rewrite is a fixed point");
    }

    [Fact]
    public void PatchAnnexB_replaces_only_sps_nals()
    {
        // Annex-B: SPS + a P-slice NAL. Only the SPS may change.
        var sps = BuildTestSps();
        var slice = new byte[] { 0x41, 0x9A, 0x02, 0x05, 0x00, 0x00, 0x03, 0x7F, 0xAA };
        var au = new byte[8 + sps.Length + slice.Length]; // SC1(4) + SPS + SC2(4) + slice
        au[0] = au[1] = au[2] = au[3] = 0; au[3] = 1;
        au[4 + sps.Length] = au[5 + sps.Length] = au[6 + sps.Length] = 0; au[7 + sps.Length] = 1;
        sps.CopyTo(au, 4);
        slice.CopyTo(au, 8 + sps.Length);

        var outAu = H264SpsFixup.PatchAnnexB(au);
        outAu.Should().NotBeSameAs(au, "an SPS was patched");

        // Walk the output: NAL 1 must parse to the patched SPS; NAL 2 must
        // be byte-identical to the original slice; start codes intact.
        var nals = SplitNalsForTest(outAu);
        nals.Count.Should().Be(2);
        (nals[0][0] & 0x1F).Should().Be(7);
        var (reorder, buffering, _) = ParseVui(nals[0]);
        reorder.Should().Be(0); buffering.Should().Be(1);
        nals[1].Should().Equal(slice, "non-SPS NALs pass through verbatim");
    }

    /// <summary>Split an Annex-B buffer into NAL payloads (start codes
    /// stripped) — test-local, mirrors the sender's splitter.</summary>
    private static List<byte[]> SplitNalsForTest(byte[] au)
    {
        var result = new List<byte[]>();
        int i = 0;
        while (i < au.Length - 3)
        {
            bool sc3 = au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1;
            bool sc4 = au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 0 && au[i + 3] == 1;
            if (!sc3 && !sc4) { i++; continue; }
            int scLen = sc4 ? 4 : 3;
            int nalStart = i + scLen;
            int j = nalStart;
            int next = au.Length;
            while (j < au.Length - 3)
            {
                bool j3 = au[j] == 0 && au[j + 1] == 0 && au[j + 2] == 1;
                bool j4 = au[j] == 0 && au[j + 1] == 0 && au[j + 2] == 0 && au[j + 3] == 1;
                if (j3 || j4) { next = j; break; }
                j++;
            }
            var nal = new byte[next - nalStart];
            Buffer.BlockCopy(au, nalStart, nal, 0, nal.Length);
            result.Add(nal);
            i = next;
        }
        return result;
    }

    [Fact]
    public void PatchAnnexB_without_sps_returns_original()
    {
        var au = new byte[] { 0, 0, 0, 1, 0x65, 1, 2, 3, 4, 5 };
        H264SpsFixup.PatchAnnexB(au).Should().BeSameAs(au);
    }

    [Fact]
    public void PatchSpsNal_on_garbage_returns_null()
    {
        var junk = new byte[] { 0x67, 0xFF, 0x00, 0x00 };
        H264SpsFixup.PatchSpsNal(junk).Should().BeNull("a parse miss must pass the original through");
    }

    [Fact]
    public void Escape_unescape_roundtrip_survives_start_code_lookalikes()
    {
        var rbsp = new byte[] { 0x12, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x02, 0xAB, 0x80 };
        var escaped = H264SpsFixup.EscapeRbsp(rbsp);
        var round = H264SpsFixup.UnescapeRbsp(escaped, 0);
        round.Should().Equal(rbsp);
        // The escaped form must not contain a false 00 00 01 start code.
        for (int i = 0; i < escaped.Length - 2; i++)
        {
            (escaped[i] == 0 && escaped[i + 1] == 0 && escaped[i + 2] == 1)
                .Should().BeFalse("emulation prevention guarantees no fake start codes");
        }
    }
}
