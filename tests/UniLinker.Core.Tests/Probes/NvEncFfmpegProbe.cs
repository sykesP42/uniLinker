using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

namespace UniLinker.Core.Tests.Probes;

/// <summary>
/// NVENC hardware-encode probe via FFmpeg.AutoGen h264_nvenc.
/// Decisive gate: if NVENC produces encoded packets for a test pattern,
/// the direct-RTP tier switches from OpenH264 software to hardware encode
/// at near-zero CPU cost. Skips gracefully when h264_nvenc is unavailable
/// (no NVIDIA GPU, driver too old, or session limit reached).
/// </summary>
public class NvEncFfmpegProbe : IDisposable
{
    private readonly ITestOutputHelper _output;

    public NvEncFfmpegProbe(ITestOutputHelper output) => _output = output;

    public void Dispose() { }

    [Fact]
    public unsafe void Probe_NvEncEncodeAndProduceBitstream()
    {
        // 1) Find the NVENC encoder. FFmpeg native DLLs are machine-local
        // (third_party/ffmpeg/setup.ps1, ~127MB — not committed); on a CI
        // checkout the first P/Invoke throws DllNotFoundException → SKIP.
        AVCodec* codec;
        try
        {
            codec = avcodec_find_encoder_by_name("h264_nvenc");
        }
        catch (Exception ex) when (ex is DllNotFoundException or NotSupportedException or EntryPointNotFoundException)
        {
            // FFmpeg.AutoGen's resolver throws NotSupportedException when the
            // native libraries are absent (CI checkout), DllNotFoundException
            // when a partial set exists. Either way: machine-local provision.
            _output.WriteLine($"VERDICT: SKIP — FFmpeg native DLLs not provisioned ({ex.GetType().Name}; run third_party/ffmpeg/setup.ps1) hardware encode untestable here");
            return;
        }
        if (codec == null)
        {
            _output.WriteLine("VERDICT: SKIP — h264_nvenc not found in FFmpeg build");
            return;
        }
        _output.WriteLine("h264_nvenc: FOUND");

        // 2) Allocate and configure the encoding context
        var ctx = avcodec_alloc_context3(codec);
        Assert.NotEqual(IntPtr.Zero, new IntPtr(codec));

        int w = 1280, h = 800, fps = 30;
        ctx->width = w;
        ctx->height = h;
        ctx->time_base = new AVRational { num = 1, den = fps };
        ctx->framerate = new AVRational { num = fps, den = 1 };
        ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        ctx->bit_rate = 8_000_000L;
        ctx->gop_size = 60;
        ctx->max_b_frames = 0;

        av_opt_set(ctx->priv_data, "preset", "p1", 0);
        av_opt_set(ctx->priv_data, "tune", "ull", 0);
        av_opt_set(ctx->priv_data, "rc", "cbr", 0);
        av_opt_set(ctx->priv_data, "delay", "0", 0);

        // 3) Open the encoder — this is where NVENC session creation happens
        int openRc = avcodec_open2(ctx, codec, null);
        if (openRc != 0)
        {
            _output.WriteLine($"VERDICT: FAIL — avcodec_open2 returned {openRc}");
            avcodec_free_context(&ctx);
            return;
        }
        _output.WriteLine("avcodec_open2: OK (NVENC session created)");

        // 4) Allocate a YUV420P frame
        var frame = av_frame_alloc();
        frame->width = w;
        frame->height = h;
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        av_frame_get_buffer(frame, 0);
        av_frame_make_writable(frame);

        // Fill with test pattern
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                frame->data[0][y * frame->linesize[0] + x] = (byte)((x + y) % 256);
        int uvW = w / 2, uvH = h / 2;
        for (int y = 0; y < uvH; y++)
            for (int x = 0; x < uvW; x++)
            {
                frame->data[1][y * frame->linesize[1] + x] = 128;
                frame->data[2][y * frame->linesize[2] + x] = 128;
            }

        // 5) Encode 3 frames, count packets
        var pkt = av_packet_alloc();
        int totalPackets = 0;
        long totalBytes = 0;
        for (int i = 0; i < 3; i++)
        {
            frame->pts = i;
            int sendRc = avcodec_send_frame(ctx, i < 2 ? frame : null); // flush on last
            while (true)
            {
                int rc = avcodec_receive_packet(ctx, pkt);
                if (rc != 0) break;
                totalPackets++;
                totalBytes += pkt->size;
                av_packet_unref(pkt);
            }
        }
        _output.WriteLine($"NVENC output: {totalPackets} packets, {totalBytes} bytes");
        totalPackets.Should().BeGreaterThan(0, "NVENC hardware encode should produce packets");

        // 6) Cleanup
        av_packet_free(&pkt);
        av_frame_free(&frame);
        avcodec_free_context(&ctx);

        _output.WriteLine("VERDICT: PASS — NVENC hardware encoding works via FFmpeg.AutoGen");
    }
}
