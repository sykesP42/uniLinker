using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace UniLinker.Plugins.ScreenMirror;

public class WgcCapture : ICapture
{
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private IDirect3DDevice? _winrtDevice;
    private ID3D11Device? _d3dDevice;        // Vortice wrapped device (for texture ops)
    private ID3D11DeviceContext? _d3dContext; // Vortice wrapped context (for texture ops)
    private ID3D11Texture2D? _stagingTexture;
    private int _width;
    private int _height;
    private int _fps;
    private bool _isCapturing;
    private Texture2DDescription _stagingDesc;
    /// <summary>Serializes the timer callback against Stop(): the callback
    /// touches every D3D object Stop frees, and Timer.Dispose does not wait
    /// for in-flight callbacks (the Unmap-on-disposed-texture AV).</summary>
    private readonly object _deviceGate = new();
    private volatile bool _stopping;

    public event Action<CaptureFrame>? FrameCaptured;
    private int _framesDelivered;
    private long _lastReadbackTicks;
    private int _minReadbackIntervalMs = 33;
    private System.Threading.Timer? _pollTimer;
    private int _emptyPolls;
    private Windows.Graphics.SizeInt32 _poolSize;
    private long _lastDownscaleMs;
    private ID3D11VideoDevice? _videoDevice;
    private ID3D11VideoContext? _videoContext;
    private ID3D11VideoProcessorEnumerator? _vpEnum;
    private ID3D11VideoProcessor? _videoProcessor;
    private ID3D11Texture2D? _scaledTexture;
    private ID3D11Texture2D? _stagingSmall;
    private int _outWidth;
    private int _outHeight;

    public bool Start(int width, int height, int fps)
    {
        _width = width > 0 ? width : 1920;
        _height = height > 0 ? height : 1080;
        _fps = fps > 0 ? fps : 30;
        // Throttle the expensive staging readback to the target fps; WGC
        // delivers frames at the display refresh rate (60/120Hz) while the
        // encoder only consumes _fps frames per second.
        _minReadbackIntervalMs = Math.Max(1, 1000 / _fps - 2);

        try
        {
            if (!CreateD3DDeviceAndWrap())
            {
                Log.Error("WGC: Failed to create D3D device");
                return false;
            }

            var displayId = GetPrimaryDisplayId();
            if (displayId == null)
            {
                Log.Error("WGC: Failed to get primary display id");
                return false;
            }

            _item = CreateItemForMonitor(displayId.HMonitor);
            if (_item == null)
            {
                Log.Error("WGC: No display item found");
                return false;
            }
            Log.Info($"WGC: item '{_item.DisplayName}' size {_item.Size.Width}x{_item.Size.Height} thread={Environment.CurrentManagedThreadId} supported={Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported()} d3dDevice={_d3dDevice != null} ctx={_d3dContext != null}");

            // Pool at NATIVE size: WGC monitor capture CROPS the display
            // content into pool-sized textures (top-left 1:1, no scaling) —
            // a 1280x800 pool on a 2560x1600 display delivered the top-left
            // quarter magnified. Capture native, then downscale on the CPU
            // (Downscale2x2) to the configured output tier.
            _poolSize = _item.Size;
            Log.Info($"WGC: poolX {_poolSize.Width}x{_poolSize.Height} (native), output tier {_width}x{_height}");

            // CreateFreeThreaded delivers FrameArrived without needing a
            // DispatcherQueue on the calling thread (capture runs on the
            // plugin's background session thread).
            // Output tier: config WxH or native when unset. The GPU scales
            // native -> tier via VideoProcessor (a 2560x1600 pool CROPS, it
            // does not scale — see DownscaleBox history).
            _outWidth = _width > 0 ? _width : _poolSize.Width;
            _outHeight = _height > 0 ? _height : _poolSize.Height;

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _poolSize);

            if (_outWidth != _poolSize.Width || _outHeight != _poolSize.Height)
            {
                // GPU downscale chain: VideoProcessor scales the full-frame
                // texture into a small GPU texture; the CPU then reads only
                // 4 MB instead of 16 MB per frame.
                _videoDevice = _d3dDevice!.QueryInterface<ID3D11VideoDevice>();
                _videoContext = _d3dContext!.QueryInterface<ID3D11VideoContext>();
                var contentDesc = new VideoProcessorContentDescription
                {
                    InputFrameFormat = VideoFrameFormat.Progressive,
                    InputWidth = unchecked((uint)_poolSize.Width),
                    InputHeight = unchecked((uint)_poolSize.Height),
                    OutputWidth = unchecked((uint)_outWidth),
                    OutputHeight = unchecked((uint)_outHeight),
                };
                _vpEnum = _videoDevice.CreateVideoProcessorEnumerator(contentDesc);
                _videoProcessor = _videoDevice.CreateVideoProcessor(_vpEnum, 0);
                _scaledTexture = _d3dDevice.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)_outWidth,
                    Height = (uint)_outHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget,
                });
                _stagingSmall = _d3dDevice.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)_outWidth,
                    Height = (uint)_outHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                });
                Log.Info($"WGC: VideoProcessor chain ready {_poolSize.Width}x{_poolSize.Height} → {_outWidth}x{_outHeight}");
            }

            _session = _framePool.CreateCaptureSession(_item);
            // NOTE: leave cursor capture on by default; toggling this property
            // before StartCapture has been flaky across Windows builds.
            _session.IsCursorCaptureEnabled = true;

            // POLLING instead of FrameArrived: with CreateFreeThreaded on a
            // background thread the event fired exactly once on this machine
            // (session started 02:41, counter stuck at #1) — mirroring froze
            // at the first frame. The frame pool keeps the latest composed
            // frame, so TryGetNextFrame in a timer works regardless of the
            // event mechanism. Same official pattern as the polling
            // CaptureSample variant.
            _isCapturing = true;
            _stopping = false; // session restart after a Stop
            _session.StartCapture();
            // 4ms poll: the drain-to-latest pattern needs a fresh frame per
            // encode - the poll interval adds up to its own length of latency
            // (avg half). 8ms -> 4ms measured free (TryGetNextFrame is cheap).
            _pollTimer = new System.Threading.Timer(
                _ => ReadLatestFrame(), null, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(4));
            Log.Info($"WGC: session started (polling @ {_fps}fps), thread={Environment.CurrentManagedThreadId}");

            Log.Info("WGC capture started (staging readback)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"WGC error: {ex}");
            return false;
        }
    }

    private void ReadLatestFrame()
    {
        if (!_isCapturing || _stopping || _framePool == null) return;

        // Stop() disposes the D3D objects this callback touches (staging
        // textures, context, device). Timer.Dispose does NOT wait for an
        // in-flight callback, so a session teardown racing a frame readback
        // Unmapped a disposed staging texture — an AV inside the native call
        // (uncatchable, killed the process; coreclr 0xc0000005 at a fixed
        // offset, thrice on 2026-11-21). The gate makes Stop wait for the
        // in-flight frame and _stopping turns away queued ones.
        lock (_deviceGate)
        {
            if (_stopping) return;
            ReadLatestFrameLocked();
        }
    }

    private void ReadLatestFrameLocked()
    {
        // DRAIN-TO-LATEST: poll fast (8 ms) and consume everything the pool
        // holds, processing only the newest frame. This caps the capture-side
        // latency at one poll interval instead of one throttle interval, and
        // it cannot fall behind — a burst of frames collapses into the latest.
        // The fps cap is applied AFTER the drain, on the encode side.
        try
        {
            Direct3D11CaptureFrame? frame = null;
            while (_framePool.TryGetNextFrame() is { } f)
            {
                frame?.Dispose();
                frame = f;
            }
            if (frame == null)
            {
                // An empty pool for this long means the DWM→pool copy pipeline
                // died (observed on this machine: exactly one frame ever
                // delivered per session). Recreate is WGC's own recovery API
                // and rebuilds that pipeline.
                _emptyPolls++;
                // x50 (~400ms of empty polls): a static desktop legitimately
                // yields no compositor frames — recreating at x10 churned the
                // pool 10x/s and hid the real "content change rate" signal.
                // The original corruption recovery still triggers, just later.
                if (_emptyPolls >= 50)
                {
                    Log.Info("WGC: poolX empty x10 — recreating frame pool");
                    _framePool.Recreate(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
                    _emptyPolls = 0;
                }
                return;
            }
            _emptyPolls = 0;

            // fps cap AFTER the drain: dropping here skips the expensive
            // readback, while the drain itself keeps the pool clean so the
            // NEXT frame is always the newest.
            var nowTicks = Environment.TickCount64;
            if (nowTicks - _lastReadbackTicks < _minReadbackIntervalMs)
            {
                frame.Dispose();
                return;
            }
            _lastReadbackTicks = nowTicks;

            var readbackStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using (frame)
            {
                if (_videoProcessor != null)
                {
                    DeliverFrameGPU(frame, readbackStart);
                }
                else
                {
                    DeliverFrame(frame, readbackStart);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"WGC frame error: {ex}");
        }
    }

    /// <summary>
    /// GPU path: scale the full-frame texture down to the output tier with
    /// the VideoProcessor, then read back only the small CPU-visible texture
    /// (4 MB at the 720p tier instead of 16 MB native).
    /// </summary>
    private void DeliverFrameGPU(Direct3D11CaptureFrame frame, long readbackStart)
    {
        var surface = frame.Surface;
        var dxgiAccess = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccessNative>(surface);
        nint dxgiSurfacePtr = nint.Zero;
        nint texturePtr = nint.Zero;
        try
        {
            Guid iidDxgiSurface = typeof(IDXGISurface).GUID;
            int hr = dxgiAccess.GetInterface(in iidDxgiSurface, out dxgiSurfacePtr);
            if (hr < 0 || dxgiSurfacePtr == nint.Zero)
            {
                Log.Error($"WGC: GetInterface(IDXGISurface) failed hr=0x{hr:X8}");
                return;
            }
            Guid iidTexture2D = typeof(ID3D11Texture2D).GUID;
            hr = Marshal.QueryInterface(dxgiSurfacePtr, in iidTexture2D, out texturePtr);
            if (hr < 0 || texturePtr == nint.Zero)
            {
                Log.Error($"WGC: QI(ID3D11Texture2D) failed hr=0x{hr:X8}");
                return;
            }
            var frameTexture = new ID3D11Texture2D(texturePtr);
            var desc = frameTexture.Description;

            using var inputView = _videoDevice!.CreateVideoProcessorInputView(
                frameTexture, _vpEnum!, new VideoProcessorInputViewDescription
                {
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0 },
                });
            using var outputView = _videoDevice.CreateVideoProcessorOutputView(
                _scaledTexture!, _vpEnum!, new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
                });

            _videoContext!.VideoProcessorSetStreamFrameFormat(_videoProcessor!, 0, VideoFrameFormat.Progressive);
            _videoContext.VideoProcessorSetStreamSourceRect(_videoProcessor!, 0, true,
                new Vortice.RawRect(0, 0, (int)desc.Width, (int)desc.Height));
            _videoContext.VideoProcessorSetStreamDestRect(_videoProcessor!, 0, true,
                new Vortice.RawRect(0, 0, _outWidth, _outHeight));

            var stream = new VideoProcessorStream
            {
                Enable = true,
                OutputIndex = 0,
                InputFrameOrField = 0,
                InputSurface = inputView,
            };
            _videoContext.VideoProcessorBlt(_videoProcessor!, outputView, 0, new[] { stream });

            // Read back the SCALED texture (4 MB) — the VP blit and the copy
            // both complete before the CPU-visible map.
            _d3dContext!.CopyResource(_stagingSmall!, _scaledTexture!);
            _d3dContext.Map(_stagingSmall!, 0, MapMode.Read,
                Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
            var rowPitch = (int)mapped.RowPitch;
            var rawData = new byte[rowPitch * _outHeight];
            Marshal.Copy(mapped.DataPointer, rawData, 0, rawData.Length);
            _d3dContext.Unmap(_stagingSmall!, 0);

            var ts = (long)(frame.SystemRelativeTime.TotalMilliseconds * 1000);
            var captureFrame = new CaptureFrame(
                D3dTexture: nint.Zero,
                Width: _outWidth,
                Height: _outHeight,
                Pitch: rowPitch,
                TimestampUs: ts,
                RawData: rawData);

            _framesDelivered++;
            if (_framesDelivered == 1)
            {
                // One-shot probe: dump the GPU-scaled frame (the encoder input)
                // to check whether the TOP of the display survives the
                // capture chain (frozen-top diagnosis).
                try
                {
                    var dumpPath = Path.Combine(Path.GetTempPath(), "wgcsmall.bin");
                    File.WriteAllBytes(dumpPath, rawData);
                    File.WriteAllText(Path.ChangeExtension(dumpPath, ".meta"),
                        $"{_outWidth}x{_outHeight} pitch={rowPitch}");
                    Log.Info($"WGC: scaled dump written {dumpPath}");
                }
                catch { }
            }
            if (_framesDelivered == 1 || _framesDelivered % 120 == 0)
            {
                var rbMs = (System.Diagnostics.Stopwatch.GetTimestamp() - readbackStart) * 1000L
                    / System.Diagnostics.Stopwatch.Frequency;
                Log.Info($"WGC frame #{_framesDelivered} {desc.Width}x{desc.Height} → {_outWidth}x{_outHeight} gpuTotal={rbMs}ms");
            }
            FrameCaptured?.Invoke(captureFrame);
        }
        finally
        {
            if (dxgiSurfacePtr != nint.Zero) Marshal.Release(dxgiSurfacePtr);
            // texturePtr is owned by the Vortice wrapper (new ID3D11Texture2D).
        }
    }

    private void DeliverFrame(Direct3D11CaptureFrame frame, long readbackStart)
    {
        {
            var surface = frame.Surface;

            // Get the underlying ID3D11Texture2D from WinRT IDirect3DSurface.
            // A raw COM cast fails on CsWinRT 2.x (the object is an
            // IInspectable wrapper); CastExtensions.As performs the QI.
            var dxgiAccess = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccessNative>(surface);
            nint dxgiSurfacePtr = nint.Zero;
            nint texturePtr = nint.Zero;

            try
            {
                // Get IDXGISurface from WinRT surface
                Guid iidDxgiSurface = typeof(IDXGISurface).GUID;
                int hr = dxgiAccess.GetInterface(in iidDxgiSurface, out dxgiSurfacePtr);
                if (hr < 0 || dxgiSurfacePtr == nint.Zero)
                {
                    Log.Error($"WGC: GetInterface(IDXGISurface) failed hr=0x{hr:X8}");
                    return;
                }

                // QI for ID3D11Texture2D from DXGI surface
                Guid iidTexture2D = typeof(ID3D11Texture2D).GUID;
                hr = Marshal.QueryInterface(dxgiSurfacePtr, in iidTexture2D, out texturePtr);
                if (hr < 0 || texturePtr == nint.Zero)
                {
                    Log.Error($"WGC: QI(ID3D11Texture2D) failed hr=0x{hr:X8}");
                    return;
                }

                // Wrap native texture in Vortice managed object
                var frameTexture = new ID3D11Texture2D(texturePtr);
                var desc = frameTexture.Description;

                // Create or resize staging texture (on first call or if dimensions change)
                if (_stagingTexture == null ||
                    desc.Width != _stagingDesc.Width ||
                    desc.Height != _stagingDesc.Height)
                {
                    _stagingTexture?.Dispose();
                    _stagingDesc = new Texture2DDescription
                    {
                        Width = desc.Width,
                        Height = desc.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                    };
                    _stagingTexture = _d3dDevice!.CreateTexture2D(_stagingDesc);
                }

                // Copy frame texture to staging (GPU -> GPU)
                _d3dContext!.CopyResource(_stagingTexture, frameTexture);

                // Map staging and read pixels (GPU -> CPU)
                _d3dContext.Map(_stagingTexture, 0, MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
                var rowPitch = (int)mapped.RowPitch;
                var dataSize = rowPitch * (int)desc.Height;
                var rawData = new byte[dataSize];
                Marshal.Copy(mapped.DataPointer, rawData, 0, dataSize);
                _d3dContext.Unmap(_stagingTexture, 0);

                var ts = (long)(frame.SystemRelativeTime.TotalMilliseconds * 1000);

                var outW = (int)desc.Width;
                var outH = (int)desc.Height;
                var outPitch = rowPitch;
                var outData = rawData;

                // Downscale to the configured OUTPUT tier (config width/height;
                // 0 = native). WGC pools CROP monitor content, they do not
                // scale it — the downscale must happen here, on the CPU, after
                // the full-frame readback.
                if (_width > 0 && _height > 0 && (outW != _width || outH != _height))
                {
                    var dsStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    outData = DownscaleBox(rawData, outW, outH, rowPitch, _width, _height);
                    outW = _width;
                    outH = _height;
                    outPitch = _width * 4;
                    _lastDownscaleMs = (System.Diagnostics.Stopwatch.GetTimestamp() - dsStart) * 1000L
                        / System.Diagnostics.Stopwatch.Frequency;
                }

                var captureFrame = new CaptureFrame(
                    D3dTexture: nint.Zero,
                    Width: outW,
                    Height: outH,
                    Pitch: outPitch,
                    TimestampUs: ts,
                    RawData: outData);

                _framesDelivered++;
                if (_framesDelivered == 1 || _framesDelivered % 120 == 0)
                {
                    var rbMs = System.Diagnostics.Stopwatch.GetTimestamp() - readbackStart;
                    Log.Info($"WGC frame #{_framesDelivered} capture {desc.Width}x{desc.Height} → out {outW}x{outH} readback={rbMs * 1000L / System.Diagnostics.Stopwatch.Frequency}ms downscale={_lastDownscaleMs}ms");
                }
                FrameCaptured?.Invoke(captureFrame);
            }
            finally
            {
                // texturePtr is OWNED by the Vortice wrapper (new ID3D11Texture2D
                // takes ownership and releases on GC/dispose) — releasing it here
                // as well double-freed the reference and corrupted the D3D
                // context: readback failed silently on every frame after the
                // first, freezing the mirror at frame #1. Only the raw
                // dxgiSurfacePtr (from GetInterface) is released manually.
                if (dxgiSurfacePtr != nint.Zero) Marshal.Release(dxgiSurfacePtr);
            }
        }
    }

    /// <summary>
    /// Box-filter downscale of a BGRA frame to a smaller size. Fast path for
    /// exact 2x1 reductions (the common 2560x1600 → 1280x800 tier); general
    /// sizes fall back to area sampling. Allocates the destination buffer.
    /// </summary>
    internal static byte[] DownscaleBox(byte[] src, int srcW, int srcH, int srcPitch, int dstW, int dstH)
    {
        var dst = new byte[dstW * 4 * dstH];
        if (srcW == dstW * 2 && srcH == dstH * 2)
        {
            // Exact 2x: average 2x2 pixel blocks.
            for (int dy = 0; dy < dstH; dy++)
            {
                int s0 = dy * 2 * srcPitch;
                int s1 = s0 + srcPitch;
                int d = dy * dstW * 4;
                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = dx * 8;
                    int dPx = d + dx * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        int v = src[s0 + sx + c] + src[s0 + sx + 4 + c]
                              + src[s1 + sx + c] + src[s1 + sx + 4 + c];
                        dst[dPx + c] = (byte)(v >> 2);
                    }
                }
            }
            return dst;
        }

        // General case: area-weighted nearest sample (quality fallback for
        // arbitrary ratios; the primary tiers are exact 2x).
        for (int dy = 0; dy < dstH; dy++)
        {
            int sy = Math.Min(srcH - 1, dy * srcH / dstH);
            int d = dy * dstW * 4;
            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = Math.Min(srcW - 1, dx * srcW / dstW) * 4;
                int s = sy * srcPitch + sx;
                dst[d + dx * 4] = src[s];
                dst[d + dx * 4 + 1] = src[s + 1];
                dst[d + dx * 4 + 2] = src[s + 2];
                dst[d + dx * 4 + 3] = src[s + 3];
            }
        }
        return dst;
    }

    public void Stop()
    {
        _isCapturing = false;
        _stopping = true;

        _pollTimer?.Dispose();
        _pollTimer = null;

        // Wait out an in-flight ReadLatestFrame before freeing what it uses
        // (see _deviceGate note at the call site). A queued callback that
        // runs after this lock returns is turned away by the _stopping check.
        lock (_deviceGate) { }

        _videoProcessor?.Dispose();
        _videoProcessor = null;
        _vpEnum?.Dispose();
        _vpEnum = null;
        _scaledTexture?.Dispose();
        _scaledTexture = null;
        _stagingSmall?.Dispose();
        _stagingSmall = null;
        _videoContext?.Dispose();
        _videoContext = null;
        _videoDevice?.Dispose();
        _videoDevice = null;

        _session?.Dispose();
        _session = null;

        _framePool?.Dispose();
        _framePool = null;

        _item = null;

        _stagingTexture?.Dispose();
        _stagingTexture = null;

        _d3dContext?.Dispose();
        _d3dContext = null;

        _d3dDevice?.Dispose();
        _d3dDevice = null;

        _winrtDevice?.Dispose();
        _winrtDevice = null;
    }

    public CaptureInfo GetInfo() => new(_width, _height, _fps, "WGC-D3D11");

    /// <summary>Actual per-frame output dimensions (native pool size when no
    /// tier was configured) — encoders initialized from the capture must use
    /// these, not the configured tier which may be 0/1920-fallback.</summary>
    public int OutputWidth => _outWidth;
    public int OutputHeight => _outHeight;

    public void Dispose() => Stop();

    // ── D3D Device Creation via raw P/Invoke + Vortice wrapping ──
    // We use raw P/Invoke here because Vortice's D3D11CreateDevice wrapper has
    // complex parameter marshaling. After creating the native device, we wrap it
    // in Vortice ID3D11Device for convenient texture operations.

    private bool CreateD3DDeviceAndWrap()
    {
        int hr = NativeD3D11CreateDevice(
            nint.Zero,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            nint.Zero,
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null, 0,
            D3D11_SDK_VERSION,
            out nint nativeDevice,
            out _,
            out nint nativeContext);

        if (hr < 0 || nativeDevice == 0)
        {
            Log.Error($"WGC: D3D11CreateDevice failed, hr=0x{hr:X8}");
            return false;
        }

        // Keep device + context alive for staging readbacks (Vortice
        // wrappers take ownership of one reference each).
        Marshal.AddRef(nativeDevice);
        _d3dDevice = new ID3D11Device(nativeDevice);
        _d3dContext = new ID3D11DeviceContext(nativeContext);
        Marshal.Release(nativeContext);

        Guid iidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        hr = Marshal.QueryInterface(nativeDevice, in iidDxgiDevice, out nint dxgiDevice);
        Marshal.Release(nativeDevice);
        if (hr < 0)
        {
            Log.Error($"WGC: QI for IDXGIDevice failed, hr=0x{hr:X8}");
            return false;
        }

        try
        {
            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint iinspectable);
            if (hr < 0)
            {
                Log.Error($"WGC: CreateDirect3D11DeviceFromDXGIDevice failed, hr=0x{hr:X8}");
                return false;
            }

            // The IInspectable pointer is already an IDirect3DDevice; the
            // projection AddRefs internally, so drop ours afterwards.
            _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(iinspectable);
            Marshal.Release(iinspectable);
            return true;
        }
        finally
        {
            Marshal.Release(dxgiDevice);
        }
    }

    // ── Display Enumeration ──

    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>
    /// Create a GraphicsCaptureItem from an HMONITOR via
    /// IGraphicsCaptureItemInterop::CreateForMonitor (works without the
    /// newer DisplayId-based API).
    /// </summary>
    private static GraphicsCaptureItem? CreateItemForMonitor(nint hMonitor)
    {
        try
        {
            var factory = WinRT.ActivationFactory.Get(
                "Windows.Graphics.Capture.GraphicsCaptureItem");
            var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
            int hr = interop.CreateForMonitor(hMonitor, GraphicsCaptureItemGuid, out nint itemPointer);
            if (hr < 0 || itemPointer == nint.Zero)
            {
                Log.Error($"WGC: CreateForMonitor hr=0x{hr:X8}");
                return null;
            }
            var item = GraphicsCaptureItem.FromAbi(itemPointer);
            Marshal.Release(itemPointer);
            return item;
        }
        catch (Exception ex)
        {
            Log.Error($"WGC: CreateForMonitor failed: {ex.Message}");
            return null;
        }
    }

    private record DisplayIdInfo(nint HMonitor);

    private static DisplayIdInfo? GetPrimaryDisplayId()
    {
        var hmon = MonitorFromWindow(GetDesktopWindow(), MONITOR_DEFAULTTOPRIMARY);
        return hmon != nint.Zero ? new DisplayIdInfo(hmon) : null;
    }

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport(User32)]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport(User32)]
    private static extern nint GetDesktopWindow();

    // ── Native P/Invoke ──
    private const string D3d11 = "d3d11.dll";
    private const string User32 = "user32.dll";
    private const uint D3D11_SDK_VERSION = 7;

    private enum D3D_DRIVER_TYPE : uint
    {
        D3D_DRIVER_TYPE_UNKNOWN = 0,
        D3D_DRIVER_TYPE_HARDWARE = 1,
        D3D_DRIVER_TYPE_REFERENCE = 2,
        D3D_DRIVER_TYPE_NULL = 3,
        D3D_DRIVER_TYPE_SOFTWARE = 4,
        D3D_DRIVER_TYPE_WARP = 5,
    }

    [Flags]
    private enum D3D11_CREATE_DEVICE_FLAG : uint
    {
        D3D11_CREATE_DEVICE_SINGLETHREADED = 0x01,
        D3D11_CREATE_DEVICE_DEBUG = 0x02,
        D3D11_CREATE_DEVICE_SWITCH_TO_REF = 0x04,
        D3D11_CREATE_DEVICE_PREVENT_INTERNAL_THREADING_OPTIMIZATIONS = 0x08,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20,
    }

    [DllImport(D3d11, EntryPoint = "D3D11CreateDevice")]
    private static extern int NativeD3D11CreateDevice(
        nint pAdapter,
        D3D_DRIVER_TYPE driverType,
        nint software,
        uint flags,
        [In] int[]? featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out nint ppDevice,
        out uint pFeatureLevel,
        out nint ppImmediateContext);

    [DllImport(D3d11)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint outDirect3D11Device);

    [DllImport(User32)]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        MonitorEnumProc lpfnEnum,
        nint dwData);

    private delegate bool MonitorEnumProc(
        nint hMonitor,
        nint hdc,
        nint lprcMonitor,
        nint dwData);

    [DllImport(User32)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoEx lpmi);

    private const uint MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

/// <summary>
/// Custom COM interface for IDirect3DDxgiInterfaceAccess (WinRT).
/// GUID: A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1
/// Used to extract native DXGI/D3D pointers from WinRT surfaces.
/// </summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccessNative
{
    [PreserveSig]
    int GetInterface(
        [In] in Guid iid,
        [Out] out IntPtr pInterface);
}

/// <summary>
/// Windows.Graphics.Capture.IGraphicsCaptureItemInterop for creating capture
/// items from native handles (works on Windows 10 1903+).
/// GUID: 3628E81B-3CAC-4C60-B7F4-23CE0E0C3356
/// </summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    [PreserveSig]
    int CreateForWindow(
        nint window,
        ref Guid iid,
        out nint item);

    [PreserveSig]
    int CreateForMonitor(
        nint hmonitor,
        ref Guid iid,
        out nint item);
}
