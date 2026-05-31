using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class WgcCapture : ICapture
{
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private IDirect3DDevice? _device;
    private int _width;
    private int _height;
    private int _fps;
    private bool _isCapturing;

    public event Action<CaptureFrame>? FrameCaptured;

    public bool Start(int width, int height, int fps)
    {
        _width = width > 0 ? width : 1920;
        _height = height > 0 ? height : 1080;
        _fps = fps > 0 ? fps : 30;

        try
        {
            // Create D3D device via native D3D11 + WinRT interop
            _device = CreateD3DDevice();
            if (_device == null)
            {
                System.Diagnostics.Debug.WriteLine("WGC: Failed to create D3D device");
                return false;
            }

            // Get primary monitor's DisplayId
            var displayId = GetPrimaryDisplayId();
            if (displayId == null)
            {
                System.Diagnostics.Debug.WriteLine("WGC: Failed to get primary display id");
                return false;
            }

            _item = GraphicsCaptureItem.TryCreateFromDisplayId(displayId.Value);
            if (_item == null)
            {
                System.Diagnostics.Debug.WriteLine("WGC: No display item found");
                return false;
            }

            _framePool = Direct3D11CaptureFramePool.Create(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);

            _session = _framePool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;

            _framePool.FrameArrived += OnFrameArrived;
            _session.StartCapture();
            _isCapturing = true;

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WGC error: {ex.Message}");
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

            var ts = (long)(frame.SystemRelativeTime.TotalMilliseconds * 1000);

            var captureFrame = new CaptureFrame(
                D3dTexture: 0, // Native texture pointer not directly accessible
                Width: frame.ContentSize.Width,
                Height: frame.ContentSize.Height,
                Pitch: frame.ContentSize.Width * 4,
                TimestampUs: ts);

            FrameCaptured?.Invoke(captureFrame);
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

        // GraphicsCaptureItem implements IClosable; we null for GC
        _item = null;

        _device?.Dispose();
        _device = null;
    }

    public CaptureInfo GetInfo() => new(_width, _height, _fps, "WGC");

    public void Dispose() => Stop();

    // ── D3D Device Creation via native D3D11 + WinRT interop ──
    // Uses CreateDirect3D11DeviceFromDXGIDevice (exported by d3d11.dll since Win10 2004)
    // to wrap a native ID3D11Device into a WinRT IDirect3DDevice.

    private static IDirect3DDevice? CreateD3DDevice()
    {
        // Create native D3D11 device
        int hr = D3D11CreateDevice(
            nint.Zero,                              // pAdapter (null = default adapter)
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            nint.Zero,                              // Software
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null, 0,
            D3D11_SDK_VERSION,
            out nint d3d11Device,
            out _, out _);

        if (hr < 0 || d3d11Device == 0)
        {
            System.Diagnostics.Debug.WriteLine($"WGC: D3D11CreateDevice failed, hr=0x{hr:X8}");
            return null;
        }

        try
        {
            // QI for IDXGIDevice from ID3D11Device
            Guid iidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3d11Device, in iidDxgiDevice, out nint dxgiDevice);
            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"WGC: QI for IDXGIDevice failed, hr=0x{hr:X8}");
                return null;
            }

            try
            {
                // Wrap DXGI device into WinRT IDirect3DDevice
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint iinspectable);
                if (hr < 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"WGC: CreateDirect3D11DeviceFromDXGIDevice failed, hr=0x{hr:X8}");
                    return null;
                }

                // Convert IInspectable to IDirect3DDevice
                Guid iidDirect3DDevice = typeof(IDirect3DDevice).GUID;
                hr = Marshal.QueryInterface(iinspectable, in iidDirect3DDevice, out nint d3dDevice);
                Marshal.Release(iinspectable);

                if (hr < 0)
                {
                    System.Diagnostics.Debug.WriteLine($"WGC: QI for IDirect3DDevice failed, hr=0x{hr:X8}");
                    return null;
                }

                return (IDirect3DDevice)Marshal.GetObjectForIUnknown(d3dDevice)!;
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(d3d11Device);
        }
    }

    // ── Display Enumeration ──
    private static DisplayId? GetPrimaryDisplayId()
    {
        DisplayId? result = null;

        EnumDisplayMonitors(nint.Zero, nint.Zero,
            (hMonitor, hdc, rc, lParam) =>
            {
                MonitorInfoEx info = default;
                info.Size = (uint)Marshal.SizeOf<MonitorInfoEx>();
                if (GetMonitorInfoW(hMonitor, ref info))
                {
                    if ((info.Flags & MONITORINFOF_PRIMARY) != 0)
                    {
                        // Use the HMONITOR value packed into DisplayId
                        result = new DisplayId((ulong)hMonitor.ToInt64());
                        return false;
                    }
                }
                return true;
            },
            nint.Zero);

        return result;
    }

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

    [DllImport(D3d11)]
    private static extern int D3D11CreateDevice(
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
