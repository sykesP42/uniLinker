using System.Runtime.InteropServices;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// H.264 encoder using Media Foundation Sink Writer with in-memory byte stream.
///
/// Pipeline: BGRA frame → MF memory buffer → IMFSample → SinkWriter → H.264 byte stream → NAL units
///
/// Uses direct COM vtable calls for IMFAttributes/IMFMediaBuffer/IMFSample interface methods
/// since they are not available as exported DLL functions — only as COM interface method slots.
///
/// IMPORTANT: All MF operations run on a dedicated MTA thread to avoid COM threading conflicts
/// with WebView2/WinForms which require STA on the main thread.
/// </summary>
public class MfEncoder : IEncoder
{
    private nint _sinkWriter;
    private nint _byteStream;
    private int _width;
    private int _height;
    private int _fps;
    private int _bitrateKbps;
    private long _frameIndex;
    private uint _streamIndex;
    private bool _initialized;

    // Thread-safe state management
    private readonly object _lock = new();
    private bool _isInitializing;
    private bool _initResult;
    private readonly ManualResetEventSlim _initComplete = new(false);

    private const string Mfplat = "mfplat.dll";
    private const string Mfreadwrite = "mfreadwrite.dll";

    // ── MF Attribute Key GUIDs (from mfapi.h) ──
    private static readonly Guid MF_MT_MAJOR_TYPE       = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    private static readonly Guid MF_MT_SUBTYPE           = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    private static readonly Guid MF_MT_AVG_BITRATE       = new("20380024-BF00-4F8D-8000-5C1C0062F5E5");
    private static readonly Guid MF_MT_INTERLACE_MODE    = new("E2724FC4-5C30-4DB6-BC0C-59AEC2F135AD");
    private static readonly Guid MF_MT_FRAME_SIZE        = new("1652C33D-D6B2-4012-B834-720CF3436C6B");
    private static readonly Guid MF_MT_FRAME_RATE        = new("C459A2CE-8E67-4B4C-B120-0B18E0EBFEBF");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("C6376A1E-8D0A-4027-BE45-6D9A0AD3BBE6");

    private static readonly Guid MFMediaType_Video  = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_ARGB32 = new("00000021-0000-0010-8000-00AA00389B71");

    // ── Event ──
#pragma warning disable CS0067
    public event Action<EncodedPacket>? PacketEncoded;
#pragma warning restore CS0067

    // ════════════════════════════════════════════════════════
    // COM vtable function-pointer delegates (safe, no unsafe)
    // ════════════════════════════════════════════════════════

    // IMFAttributes (vtable offsets after IUnknown slots 0-2)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSetUint32(IntPtr pThis, ref Guid guidKey, uint value);       // slot 19
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSetUint64(IntPtr pThis, ref Guid guidKey, ulong value);      // slot 20
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSetGuid(IntPtr pThis, ref Guid guidKey, ref Guid guidValue); // slot 22

    // IMFMediaBuffer (offsets after IUnknown)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComLock(IntPtr pThis, out IntPtr ppData, out uint pcbMaxLength,
        out uint pcbCurrentLength);                                                       // slot 3
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComUnlock(IntPtr pThis);                                          // slot 4
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSetCurLen(IntPtr pThis, uint cbCurrentLength);                // slot 6

    // IMFSample (offsets after IUnknown)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSampleSetTime(IntPtr pThis, long hnsSampleTime);              // slot 6
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSampleSetDuration(IntPtr pThis, long hnsSampleDuration);       // slot 8
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSampleAddBuffer(IntPtr pThis, IntPtr pBuffer);                // slot 12

    // IMFByteStream (offsets after IUnknown)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComGetCurPos(IntPtr pThis, out ulong pqwPosition);               // slot 6
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComSetCurPos(IntPtr pThis, ulong qwPosition);                    // slot 7
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ComRead(IntPtr pThis, IntPtr pb, uint cb, out uint pcbRead);     // slot 9

    // ── Helper: extract vtable method delegate ──

    private static T Vtbl<T>(IntPtr pCom, int slot) where T : Delegate
    {
        IntPtr vtbl = Marshal.ReadIntPtr(pCom);
        IntPtr methodPtr = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
    }

    // ════════════════════════════════════════════════════════
    // P/Invoke — exported MF API functions
    // ════════════════════════════════════════════════════════

    [DllImport(Mfplat)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport(Mfplat)]
    private static extern int MFShutdown();

    [DllImport(Mfplat)]
    private static extern int MFCreateMediaType(out nint ppMediaType);

    [DllImport(Mfplat)]
    private static extern int MFCreateMemoryByteStream(out nint ppByteStream);

    [DllImport(Mfplat)]
    private static extern int MFCreateSample(out nint ppSample);

    [DllImport(Mfplat)]
    private static extern int MFCreateMemoryBuffer(uint cbMaxLength, out nint ppBuffer);

    [DllImport(Mfreadwrite)]
    private static extern int MFCreateSinkWriterFromURL(
        string? pwszURL, nint pByteStream, nint pAttributes, out nint ppSinkWriter);

    [DllImport(Mfreadwrite)]
    private static extern int MFSinkWriterAddStream(
        nint pSinkWriter, nint pMediaType, out uint pdwStreamIndex);

    [DllImport(Mfreadwrite)]
    private static extern int MFSinkWriterSetInputMediaType(
        nint pSinkWriter, uint dwStreamIndex, nint pInputType, nint pEncodingParams);

    [DllImport(Mfreadwrite)]
    private static extern int MFSinkWriterBeginWriting(nint pSinkWriter);

    [DllImport(Mfreadwrite)]
    private static extern int MFSinkWriterWriteSample(
        nint pSinkWriter, uint dwStreamIndex, nint pSample);

    [DllImport(Mfreadwrite)]
    private static extern int MFSinkWriterFinalize(nint pSinkWriter);

    // ════════════════════════════════════════════════════════
    // IEncoder Implementation
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Check if Media Foundation is available.
    /// Runs MFStartup/MFShutdown on a background MTA thread to avoid COM conflicts.
    /// </summary>
    public static bool IsAvailable()
    {
        // Run on a separate thread to avoid affecting main thread's COM apartment state
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                int hr = MFStartup(0x20070, 0);
                if (hr >= 0)
                {
                    MFShutdown();
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetResult(false);
                }
            }
            catch
            {
                tcs.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        return tcs.Task.GetAwaiter().GetResult();
    }

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        // Clamp to valid values
        _width = Math.Clamp(width, 320, 3840);
        _height = Math.Clamp(height, 240, 2160);
        _fps = Math.Clamp(fps, 1, 120);
        _bitrateKbps = Math.Clamp(bitrateKbps, 100, 100000);

        // Initialize MF on a dedicated MTA thread
        var initThread = new Thread(() =>
        {
            try
            {
                if (MFStartup(0x20070, 0) < 0)
                {
                    lock (_lock) { _initResult = false; }
                    return;
                }

                if (MFCreateMemoryByteStream(out _byteStream) < 0)
                {
                    CleanUp();
                    lock (_lock) { _initResult = false; }
                    return;
                }

                if (MFCreateSinkWriterFromURL(null, _byteStream, nint.Zero, out _sinkWriter) < 0)
                {
                    CleanUp();
                    lock (_lock) { _initResult = false; }
                    return;
                }

                nint outType = CreateOutputMediaType();
                if (MFSinkWriterAddStream(_sinkWriter, outType, out _streamIndex) < 0)
                {
                    CleanUp();
                    lock (_lock) { _initResult = false; }
                    return;
                }

                nint inType = CreateInputMediaType();
                if (MFSinkWriterSetInputMediaType(_sinkWriter, _streamIndex, inType, nint.Zero) < 0)
                {
                    CleanUp();
                    lock (_lock) { _initResult = false; }
                    return;
                }

                if (MFSinkWriterBeginWriting(_sinkWriter) < 0)
                {
                    CleanUp();
                    lock (_lock) { _initResult = false; }
                    return;
                }

                _initialized = true;
                lock (_lock) { _initResult = true; }
                System.Diagnostics.Debug.WriteLine(
                    $"MF: H.264 encoder initialized ({_width}x{_height} @{_fps}fps, {_bitrateKbps}kbps)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MF encoder init error: {ex.Message}");
                CleanUp();
                lock (_lock) { _initResult = false; }
            }
            finally
            {
                _initComplete.Set();
            }
        })
        {
            IsBackground = true,
        };

        initThread.Start();
        _initComplete.Wait(TimeSpan.FromSeconds(10));

        lock (_lock)
        {
            return _initResult;
        }
    }

    public void Encode(CaptureFrame frame)
    {
        if (!_initialized || _sinkWriter == 0) return;

        try
        {
            // Strip row-stride padding to produce tight BGRA data
            var src = frame.RawData ?? [];
            byte[] bgra = StripPitch(src, frame.Width, frame.Height, frame.Pitch);

            // Create MF sample from BGRA data
            nint sample = CreateBgraSample(bgra, _frameIndex);

            // Write to SinkWriter (MF inserts color converter ARGB32 → NV12 → H.264)
            int hr = MFSinkWriterWriteSample(_sinkWriter, _streamIndex, sample);
            Marshal.Release(sample);

            if (hr >= 0)
            {
                _frameIndex++;
                DrainEncodedData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MF encode error: {ex.Message}");
        }
    }

    public EncoderInfo GetInfo() =>
        new("h264_mf", _bitrateKbps, IsHardware: false);

    public void Dispose() => CleanUp();

    // ════════════════════════════════════════════════════════
    // Media Type Helpers
    // ════════════════════════════════════════════════════════

    private nint CreateOutputMediaType()
    {
        // Copy static readonly GUIDs to locals — C# prevents passing static
        // readonly fields as ref in instance methods (CS0199).
        HResult.Check(MFCreateMediaType(out nint mt));

        var kMajor = MF_MT_MAJOR_TYPE;       var vVideo = MFMediaType_Video;
        MfSetGuid(mt, ref kMajor, ref vVideo);
        var kSub = MF_MT_SUBTYPE;            var vH264 = MFVideoFormat_H264;
        MfSetGuid(mt, ref kSub, ref vH264);
        var kBitrate = MF_MT_AVG_BITRATE;
        MfSetUint32(mt, ref kBitrate, (uint)(_bitrateKbps * 1000));
        var kInterlace = MF_MT_INTERLACE_MODE;
        MfSetUint32(mt, ref kInterlace, 2); // MFVideoInterlace_Progressive
        var kFsize = MF_MT_FRAME_SIZE;
        MfSetUint64(mt, ref kFsize, PackHiLo(_width, _height));
        var kFrate = MF_MT_FRAME_RATE;
        MfSetUint64(mt, ref kFrate, PackHiLo(_fps, 1));
        var kPar = MF_MT_PIXEL_ASPECT_RATIO;
        MfSetUint64(mt, ref kPar, PackHiLo(1, 1));

        return mt;
    }

    private nint CreateInputMediaType()
    {
        HResult.Check(MFCreateMediaType(out nint mt));

        var kMajor = MF_MT_MAJOR_TYPE;       var vVideo = MFMediaType_Video;
        MfSetGuid(mt, ref kMajor, ref vVideo);
        var kSub = MF_MT_SUBTYPE;            var vArgb = MFVideoFormat_ARGB32;
        MfSetGuid(mt, ref kSub, ref vArgb);
        var kInterlace = MF_MT_INTERLACE_MODE;
        MfSetUint32(mt, ref kInterlace, 2);
        var kFsize = MF_MT_FRAME_SIZE;
        MfSetUint64(mt, ref kFsize, PackHiLo(_width, _height));
        var kFrate = MF_MT_FRAME_RATE;
        MfSetUint64(mt, ref kFrate, PackHiLo(_fps, 1));
        var kPar = MF_MT_PIXEL_ASPECT_RATIO;
        MfSetUint64(mt, ref kPar, PackHiLo(1, 1));

        return mt;
    }

    // Pack two 32-bit values into one 64-bit (MF convention: hi=high, lo=low or w/h)
    private static ulong PackHiLo(int hi, int lo) =>
        ((ulong)(uint)hi << 32) | (uint)lo;

    // ── COM vtable attribute helpers ──

    private static void MfSetGuid(nint pAttrs, ref Guid key, ref Guid value)
    {
        var del = Vtbl<ComSetGuid>(pAttrs, 22);
        HResult.Check(del(pAttrs, ref key, ref value));
    }

    private static void MfSetUint32(nint pAttrs, ref Guid key, uint value)
    {
        var del = Vtbl<ComSetUint32>(pAttrs, 19);
        HResult.Check(del(pAttrs, ref key, value));
    }

    private static void MfSetUint64(nint pAttrs, ref Guid key, ulong value)
    {
        var del = Vtbl<ComSetUint64>(pAttrs, 20);
        HResult.Check(del(pAttrs, ref key, value));
    }

    // ════════════════════════════════════════════════════════
    // Sample Creation
    // ════════════════════════════════════════════════════════

    private nint CreateBgraSample(byte[] bgra, long frameIndex)
    {
        // Create MF memory buffer
        HResult.Check(MFCreateMemoryBuffer((uint)bgra.Length, out nint buffer));

        try
        {
            // Lock buffer and copy BGRA data
            var lockDel = Vtbl<ComLock>(buffer, 3);
            HResult.Check(lockDel(buffer, out IntPtr pData, out _, out _));
            try
            {
                Marshal.Copy(bgra, 0, pData, bgra.Length);
            }
            finally
            {
                var unlockDel = Vtbl<ComUnlock>(buffer, 4);
                unlockDel(buffer);
            }

            // Set buffer length
            var setLenDel = Vtbl<ComSetCurLen>(buffer, 6);
            HResult.Check(setLenDel(buffer, (uint)bgra.Length));

            // Create sample and add buffer
            HResult.Check(MFCreateSample(out nint sample));
            try
            {
                var addBufDel = Vtbl<ComSampleAddBuffer>(sample, 12);
                HResult.Check(addBufDel(sample, buffer));

                // Set timestamp and duration (100ns units)
                long sampleTime = frameIndex * 10_000_000 / _fps;
                var setTimeDel = Vtbl<ComSampleSetTime>(sample, 6);
                HResult.Check(setTimeDel(sample, sampleTime));

                long sampleDuration = 10_000_000 / _fps;
                var setDurDel = Vtbl<ComSampleSetDuration>(sample, 8);
                HResult.Check(setDurDel(sample, sampleDuration));
            }
            catch
            {
                Marshal.Release(sample);
                throw;
            }
            return sample;
        }
        finally
        {
            Marshal.Release(buffer);
        }
    }

    // ════════════════════════════════════════════════════════
    // Byte Stream Drain — read encoded H.264 from MF byte stream
    // ════════════════════════════════════════════════════════

    private long _lastReadPos;

    private void DrainEncodedData()
    {
        if (_byteStream == 0) return;

        // Query current byte stream position
        var getPosDel = Vtbl<ComGetCurPos>(_byteStream, 6);
        if (getPosDel(_byteStream, out ulong currentPos) < 0) return;

        if (currentPos <= (ulong)_lastReadPos) return;

        ulong available = currentPos - (ulong)_lastReadPos;
        if (available == 0) return;

        // Clamp read size to avoid huge allocations on spurious data
        if (available > 4 * 1024 * 1024) available = 4 * 1024 * 1024;

        byte[] data = new byte[available];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            // Seek to last-read position
            var setPosDel = Vtbl<ComSetCurPos>(_byteStream, 7);
            setPosDel(_byteStream, (ulong)_lastReadPos);

            // Read encoded data
            var readDel = Vtbl<ComRead>(_byteStream, 9);
            IntPtr pData = handle.AddrOfPinnedObject();
            if (readDel(_byteStream, pData, (uint)data.Length, out uint bytesRead) >= 0
                && bytesRead > 0)
            {
                ParseNalUnits(data, 0, (int)bytesRead);
            }

            // Restore position to end for subsequent writes
            setPosDel(_byteStream, currentPos);
        }
        finally
        {
            handle.Free();
        }

        _lastReadPos = (long)currentPos;
    }

    // ════════════════════════════════════════════════════════
    // H.264 NAL Unit Parsing
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Parse H.264 NAL units from the byte stream.
    /// Handles both Annex B (start-code delimited) and AVCC (4-byte length prefix) formats.
    /// Fires PacketEncoded for each complete NAL unit.
    /// </summary>
    private void ParseNalUnits(byte[] data, int offset, int count)
    {
        int end = offset + count;
        if (count < 4) return;

        // Check for Annex B start codes
        bool isAnnexB = false;
        for (int i = offset; i <= end - 3; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && (data[i + 2] == 1 ||
                (i + 3 < end && data[i + 2] == 0 && data[i + 3] == 1)))
            {
                isAnnexB = true;
                break;
            }
        }

        if (isAnnexB)
            ParseAnnexB(data, offset, count);
        else
            ParseAvcc(data, offset, count);
    }

    private void ParseAnnexB(byte[] data, int offset, int count)
    {
        int end = offset + count;

        // Build list of start code positions
        var starts = new List<int>();
        for (int i = offset; i <= end - 3; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                if (data[i + 2] == 1)
                {
                    starts.Add(i);
                    i += 2;
                }
                else if (i + 3 < end && data[i + 2] == 0 && data[i + 3] == 1)
                {
                    starts.Add(i);
                    i += 3;
                }
            }
        }

        if (starts.Count == 0) return;

        for (int i = 0; i < starts.Count; i++)
        {
            int nalStart = starts[i];
            int nextStart = (i + 1 < starts.Count) ? starts[i + 1] : end;

            // Skip start code bytes
            int codeLen = 4;
            if (nalStart + 3 <= end && data[nalStart] == 0 && data[nalStart + 1] == 0
                && data[nalStart + 2] == 0 && data[nalStart + 3] == 1)
                codeLen = 4;
            else if (nalStart + 2 <= end && data[nalStart] == 0 && data[nalStart + 1] == 0
                     && data[nalStart + 2] == 1)
                codeLen = 3;

            int nalDataOff = nalStart + codeLen;
            int nalSize = nextStart - nalDataOff;
            if (nalSize <= 0) continue;

            byte[] nal = new byte[nalSize];
            Buffer.BlockCopy(data, nalDataOff, nal, 0, nalSize);

            EmitNal(nal);
        }
    }

    private void ParseAvcc(byte[] data, int offset, int count)
    {
        int pos = offset;
        int end = offset + count;

        while (pos + 4 <= end)
        {
            // Read 4-byte big-endian length
            int nalSize = (data[pos] << 24) | (data[pos + 1] << 16)
                        | (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;

            if (nalSize <= 0 || pos + nalSize > end) break;

            byte[] nal = new byte[nalSize];
            Buffer.BlockCopy(data, pos, nal, 0, nalSize);
            pos += nalSize;

            EmitNal(nal);
        }
    }

    private void EmitNal(byte[] nal)
    {
        if (nal.Length == 0) return;

        int nalType = nal[0] & 0x1F;
        bool isKeyFrame = nalType == 5;  // IDR slice (CodedSliceIdr)
        long timestamp = _frameIndex * 1_000_000 / _fps;

        PacketEncoded?.Invoke(new EncodedPacket(nal, timestamp, isKeyFrame));
    }

    // ════════════════════════════════════════════════════════
    // BGRA Utility
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Strip row-stride padding from BGRA data.
    /// WGC captures with D3D11 staging textures may have a stride larger than width*4.
    /// MF expects tight (packed) pixel data with no stride padding.
    /// </summary>
    private static byte[] StripPitch(byte[] data, int width, int height, int pitch)
    {
        int tightRow = width * 4;
        if (pitch <= 0 || pitch == tightRow)
            return data;

        int totalTight = tightRow * height;
        byte[] result = new byte[totalTight];

        for (int y = 0; y < height; y++)
        {
            int srcOff = y * pitch;
            int dstOff = y * tightRow;
            int copyLen = Math.Min(tightRow, data.Length - srcOff);
            Buffer.BlockCopy(data, srcOff, result, dstOff, copyLen);
        }

        return result;
    }

    // ════════════════════════════════════════════════════════
    // Cleanup
    // ════════════════════════════════════════════════════════

    private void CleanUp()
    {
        if (_sinkWriter != 0)
        {
            try { MFSinkWriterFinalize(_sinkWriter); } catch { }
            Marshal.Release(_sinkWriter);
            _sinkWriter = 0;
        }
        if (_byteStream != 0)
        {
            Marshal.Release(_byteStream);
            _byteStream = 0;
        }
        try { MFShutdown(); } catch { }
        _initialized = false;
    }
}

// ── HRESULT helper ──

internal static class HResult
{
    /// <summary>
    /// Check HRESULT and throw COMException on failure.
    /// MF uses standard COM HRESULT: S_OK = 0, errors are negative.
    /// </summary>
    internal static void Check(int hr)
    {
        if (hr < 0)
            throw new COMException($"MF operation failed with HRESULT 0x{hr:X8}", hr);
    }
}
