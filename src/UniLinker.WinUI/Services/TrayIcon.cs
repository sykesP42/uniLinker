using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace UniLinker.WinUI.Services;

/// <summary>
/// Simple system tray icon using Windows Shell_NotifyIcon API
/// </summary>
public class TrayIcon : IDisposable
{
    private nint _hwnd;
    private int _id = 1;
    private bool _isCreated;

    public event Action? ShowClicked;
    public event Action? ExitClicked;

    // Shell_NotifyIcon constants
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    // Menu commands
    private const int IDM_SHOW = 1000;
    private const int IDM_EXIT = 1001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(nint hMenu, int uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, int Msg, nint wParam, nint lParam);

    private const int GWLP_WNDPROC = -4;
    private const int WM_DESTROY = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private nint _originalWndProc;
    private delegate nint WndProcDelegate(nint hWnd, int msg, nint wParam, nint lParam);
    private WndProcDelegate? _wndProcDelegate;

    public void Initialize(nint hwnd, string tooltip = "UniLinker")
    {
        _hwnd = hwnd;

        // Subclass window to handle tray icon messages
        _wndProcDelegate = WndProc;
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _originalWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, wndProcPtr);

        // Create tray icon
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = _id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadIcon(nint.Zero, 32512), // IDI_APPLICATION
            szTip = tooltip
        };

        Shell_NotifyIcon(NIM_ADD, ref data);
        _isCreated = true;
    }

    private nint WndProc(nint hWnd, int msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = lParam.ToInt32();
            if (mouseMsg == WM_LBUTTONUP)
            {
                ShowClicked?.Invoke();
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
            }
        }
        else if (msg == WM_DESTROY)
        {
            RemoveTrayIcon();
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();

        AppendMenu(hMenu, 0x00000000, IDM_SHOW, "Show Window");
        AppendMenu(hMenu, 0x00000800, 0, ""); // Separator
        AppendMenu(hMenu, 0x00000000, IDM_EXIT, "Exit");

        GetCursorPos(out var pt);

        SetForegroundWindow(_hwnd);

        TrackPopupMenu(hMenu, 0x0008 | 0x0010, pt.X, pt.Y, 0, _hwnd, nint.Zero);

        DestroyMenu(hMenu);
    }

    public void HandleMenuCommand(int commandId)
    {
        if (commandId == IDM_SHOW)
        {
            ShowClicked?.Invoke();
        }
        else if (commandId == IDM_EXIT)
        {
            ExitClicked?.Invoke();
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isCreated) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
            uFlags = NIF_INFO,
            szInfoTitle = title,
            szInfo = message,
            dwInfoFlags = 0x00000001 // NIIF_INFO
        };

        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_isCreated) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
            uFlags = NIF_TIP,
            szTip = tooltip
        };

        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (!_isCreated) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id
        };

        Shell_NotifyIcon(NIM_DELETE, ref data);
        _isCreated = false;
    }

    public void Dispose()
    {
        RemoveTrayIcon();

        if (_originalWndProc != nint.Zero && _hwnd != nint.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _originalWndProc);
            _originalWndProc = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }
}