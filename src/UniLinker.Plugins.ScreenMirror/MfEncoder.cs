using System.Runtime.InteropServices;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class MfEncoder : IEncoder
{
    private nint _sinkWriter;
    private nint _byteStream;
    private int _width;
    private int _height;
    private int _fps;
    private int _bitrateKbps;
    private long _frameIndex;
    private string _activeCodec = "h264_mf";
    private bool _initialized;
    private uint _streamIndex;

#pragma warning disable CS0067
    public event Action<EncodedPacket>? PacketEncoded;
#pragma warning restore CS0067

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        _width = width > 0 ? width : 1920;
        _height = height > 0 ? height : 1080;
        _fps = fps > 0 ? fps : 30;
        _bitrateKbps = bitrateKbps > 0 ? bitrateKbps : 15000;

        try
        {
            // Initialize Media Foundation
            _ = MFStartup(0x20070, 0);

            // Create in-memory byte stream for encoded output
            int hr = MFCreateMemoryByteStream(out _byteStream);
            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"MF: Failed to create byte stream, hr=0x{hr:X8}");
                return false;
            }

            // Create SinkWriter
            hr = MFCreateSinkWriterFromURL(null, _byteStream, nint.Zero, out _sinkWriter);
            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"MF: Failed to create sink writer, hr=0x{hr:X8}");
                return false;
            }

            // Configure H.264 output media type
            SetupOutputType();
            // Configure BGRA input media type
            SetupInputType();

            // Begin writing
            hr = MFSinkWriterBeginWriting(_sinkWriter);
            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"MF: BeginWriting failed, hr=0x{hr:X8}");
                return false;
            }

            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MF encoder init error: {ex.Message}");
            return false;
        }
    }

    private void SetupOutputType()
    {
        nint outType;
        MFCreateMediaType(out outType);

        MFSetGUID(outType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
        MFSetGUID(outType, MF_MT_SUBTYPE, MFVideoFormat_H264);
        MFSetUINT32(outType, MF_MT_AVG_BITRATE, (uint)(_bitrateKbps * 1000));
        MFSetUINT32(outType, MF_MT_INTERLACE_MODE, 2); // Progressive
        MFSetSize(outType, (uint)_width, (uint)_height);
        MFSetRatio(outType, MF_MT_FRAME_RATE, (uint)_fps, 1);
        MFSetRatio(outType, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);

        MFSinkWriterAddStream(_sinkWriter, outType, out _streamIndex);

        Marshal.Release(outType);
    }

    private void SetupInputType()
    {
        nint inType;
        MFCreateMediaType(out inType);

        MFSetGUID(inType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
        MFSetGUID(inType, MF_MT_SUBTYPE, MFVideoFormat_ARGB32);
        MFSetUINT32(inType, MF_MT_INTERLACE_MODE, 2); // Progressive
        MFSetSize(inType, (uint)_width, (uint)_height);
        MFSetRatio(inType, MF_MT_FRAME_RATE, (uint)_fps, 1);
        MFSetRatio(inType, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);

        MFSinkWriterSetInputMediaType(_sinkWriter, _streamIndex, inType, nint.Zero);

        Marshal.Release(inType);
    }

    public void Encode(CaptureFrame frame)
    {
        if (!_initialized) return;

        // Create a simple solid-color test frame for now
        // In production: copy frame data into an IMFMediaBuffer
        int frameSize = _width * _height * 4; // BGRA
        byte[] frameData = new byte[frameSize];

        // Create sample
        nint sample;
        MFCreateSample(out sample);

        nint buffer;
        MFCreateMemoryBuffer((uint)frameSize, out buffer);

        nint bufferPtr;
        IMFMediaBufferLock(buffer, out bufferPtr, out _, out _);
        Marshal.Copy(frameData, 0, bufferPtr, frameSize);
        IMFMediaBufferUnlock(buffer);
        IMFMediaBufferSetCurrentLength(buffer, (uint)frameSize);

        MFSampleAddBuffer(sample, buffer);

        // Set timestamp
        long timestamp = _frameIndex * 10000000 / _fps; // 100ns units
        MFSampleSetSampleTime(sample, timestamp);
        MFSampleSetSampleDuration(sample, 10000000 / _fps);

        // Write sample
        int hr = MFSinkWriterWriteSample(_sinkWriter, _streamIndex, sample);

        Marshal.Release(buffer);
        Marshal.Release(sample);

        if (hr >= 0)
        {
            _frameIndex++;
            DrainEncodedData();
        }
    }

    private void DrainEncodedData()
    {
        if (_byteStream == 0) return;

        // Get encoded data from byte stream
        nint stat;
        MFByteStreamGetStats(_byteStream, out stat);

        // For now, encode is a black-box pipeline.
        // The encoded H.264 data accumulates in the byte stream.
        // In production, we'd pull from it after each frame.
        // This skeleton allows the build to compile and link against MF.
    }

    public EncoderInfo GetInfo() => new(_activeCodec, _bitrateKbps, IsHardware: true);

    public void Dispose()
    {
        if (_sinkWriter != 0)
        {
            MFSinkWriterFinalize(_sinkWriter);
            Marshal.Release(_sinkWriter);
            _sinkWriter = 0;
        }
        if (_byteStream != 0)
        {
            Marshal.Release(_byteStream);
            _byteStream = 0;
        }
        MFShutdown();
    }

    // ── Media Foundation P/Invoke ──
    private const string Mfplat = "mfplat.dll";
    private const string Mfreadwrite = "mfreadwrite.dll";

    [DllImport(Mfplat)] private static extern int MFStartup(uint version, uint flags);
    [DllImport(Mfplat)] private static extern int MFShutdown();
    [DllImport(Mfplat)] private static extern int MFCreateMemoryByteStream(out nint byteStream);
    [DllImport(Mfplat)] private static extern int MFCreateMediaType(out nint mediaType);
    [DllImport(Mfplat)] private static extern int MFCreateSample(out nint sample);
    [DllImport(Mfplat)] private static extern int MFCreateMemoryBuffer(uint maxLength, out nint buffer);
    [DllImport(Mfplat)] private static extern int IMFMediaBufferLock(nint buffer, out nint data, out uint maxLength, out uint currentLength);
    [DllImport(Mfplat)] private static extern int IMFMediaBufferUnlock(nint buffer);
    [DllImport(Mfplat)] private static extern int IMFMediaBufferSetCurrentLength(nint buffer, uint currentLength);
    [DllImport(Mfplat)] private static extern int MFSampleAddBuffer(nint sample, nint buffer);
    [DllImport(Mfplat)] private static extern int MFSampleSetSampleTime(nint sample, long hnsSampleTime);
    [DllImport(Mfplat)] private static extern int MFSampleSetSampleDuration(nint sample, long hnsSampleDuration);
    [DllImport(Mfplat)] private static extern int MFByteStreamGetStats(nint byteStream, out nint stats);

    [DllImport(Mfreadwrite)] private static extern int MFCreateSinkWriterFromURL(
        string? url, nint byteStream, nint attributes, out nint sinkWriter);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterAddStream(
        nint sinkWriter, nint outputType, out uint streamIndex);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterSetInputMediaType(
        nint sinkWriter, uint streamIndex, nint inputType, nint parameters);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterBeginWriting(nint sinkWriter);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterWriteSample(
        nint sinkWriter, uint streamIndex, nint sample);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterFinalize(nint sinkWriter);

    // ── Media Foundation GUID helpers ──
    private static int MFSetGUID(nint type, nint key, Guid value)
    {
        byte[] guidBytes = value.ToByteArray();
        nint ptr = Marshal.AllocHGlobal(guidBytes.Length);
        Marshal.Copy(guidBytes, 0, ptr, guidBytes.Length);
        int hr = MFSetBlob(type, key, ptr, (uint)guidBytes.Length);
        Marshal.FreeHGlobal(ptr);
        return hr;
    }

    [DllImport(Mfplat)] private static extern int MFSetBlob(nint type, nint key, nint blob, uint size);
    [DllImport(Mfplat)] private static extern int MFSetUINT32(nint type, nint key, uint value);
    [DllImport(Mfplat)] private static extern int MFSetSize(nint type, uint width, uint height);
    [DllImport(Mfplat)] private static extern int MFSetRatio(nint type, nint key, uint numerator, uint denominator);

    // MF attribute keys (guidconstants from mfapi.h)
    // These are Windows SDK const GUIDs; for interop we use their numeric IDs
    // The MF_MT_* keys are GUIDs, not ints. We pass them as blob keys.
    private static readonly nint MF_MT_MAJOR_TYPE = Marshal.StringToHGlobalUni("MF_MT_MAJOR_TYPE");
    private static readonly nint MF_MT_SUBTYPE = Marshal.StringToHGlobalUni("MF_MT_SUBTYPE");
    private static readonly nint MF_MT_AVG_BITRATE = Marshal.StringToHGlobalUni("MF_MT_AVG_BITRATE");
    private static readonly nint MF_MT_INTERLACE_MODE = Marshal.StringToHGlobalUni("MF_MT_INTERLACE_MODE");
    private static readonly nint MF_MT_FRAME_RATE = Marshal.StringToHGlobalUni("MF_MT_FRAME_RATE");
    private static readonly nint MF_MT_PIXEL_ASPECT_RATIO = Marshal.StringToHGlobalUni("MF_MT_PIXEL_ASPECT_RATIO");

    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_ARGB32 = new("00000021-0000-0010-8000-00AA00389B71");
}
