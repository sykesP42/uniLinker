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

    public event Action<CaptureFrame>? FrameCaptured;
    private int _framesDelivered;

    public bool Start(int width, int height, int fps)
    {
        _width = width > 0 ? width : 1920;
        _height = height > 0 ? height : 1080;
        _fps = fps > 0 ? fps : 30;

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

            // CreateFreeThreaded delivers FrameArrived without needing a
            // DispatcherQueue on the calling thread (capture runs on the
            // plugin's background session thread).
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);

            _session = _framePool.CreateCaptureSession(_item);
            // NOTE: leave cursor capture on by default; toggling this property
            // before StartCapture has been flaky across Windows builds.
            _session.IsCursorCaptureEnabled = true;

            _framePool.FrameArrived += OnFrameArrived;
            // Must be set before StartCapture: on a static desktop WGC only
            // delivers the initial frame, which would otherwise race this
            // flag and be dropped forever.
            _isCapturing = true;
            _session.StartCapture();
            Log.Info($"WGC: session started, thread={Environment.CurrentManagedThreadId}, framePoolIsFreeThreaded=yes");

            Log.Info("WGC capture started (staging readback)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"WGC error: {ex}");
            return false;
        }
    }

    private void OnFrameArrived(
        Direct3D11CaptureFramePool sender, object args)
    {
        if (!_isCapturing) return;

        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

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
                    System.Diagnostics.Debug.WriteLine($"WGC: GetInterface(IDXGISurface) failed hr=0x{hr:X8}");
                    return;
                }

                // QI for ID3D11Texture2D from DXGI surface
                Guid iidTexture2D = typeof(ID3D11Texture2D).GUID;
                hr = Marshal.QueryInterface(dxgiSurfacePtr, in iidTexture2D, out texturePtr);
                if (hr < 0 || texturePtr == nint.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"WGC: QI(ID3D11Texture2D) failed hr=0x{hr:X8}");
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

                var captureFrame = new CaptureFrame(
                    D3dTexture: nint.Zero,
                    Width: (int)desc.Width,
                    Height: (int)desc.Height,
                    Pitch: rowPitch,
                    TimestampUs: ts,
                    RawData: rawData);

                _framesDelivered++;
                if (_framesDelivered == 1 || _framesDelivered % 120 == 0)
                {
                    Log.Info($"WGC frame #{_framesDelivered} {desc.Width}x{desc.Height} pitch={rowPitch}");
                }
                FrameCaptured?.Invoke(captureFrame);
            }
            finally
            {
                if (texturePtr != nint.Zero) Marshal.Release(texturePtr);
                if (dxgiSurfacePtr != nint.Zero) Marshal.Release(dxgiSurfacePtr);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WGC frame error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _isCapturing = false;

        if (_framePool != null)
            _framePool.FrameArrived -= OnFrameArrived;

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
