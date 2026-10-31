/**
 * @Module: CraneWifi.WidgetApp.Utils.NativeNotifyIcon
 * @Purpose: Quản lý biểu tượng khay hệ thống (System Tray Notification Icon) thuần Windows Win32 API qua shell32 Shell_NotifyIcon và HwndSource, không cần kéo dependency cồng kềnh WinForms (System.Windows.Forms).
 * @Dependencies: Windows shell32.dll, user32.dll, System.Windows.Interop.
 * @Reusability: Cực kỳ gọn nhẹ, có thể tái sử dụng cho bất kỳ ứng dụng WPF nào cần System Tray icon native hiệu năng cao.
 */

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CraneWifi.WidgetApp.Utils;

public class NativeNotifyIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;

    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly HwndSource _hwndSource;
    private readonly uint _id = 1;
    private bool _isCreated = false;
    private ContextMenu? _contextMenu;

    public Action? DoubleClickAction { get; set; }

    public NativeNotifyIcon(string tooltip = "Crane Wi-Fi Controller", ContextMenu? contextMenu = null)
    {
        _contextMenu = contextMenu;

        var parameters = new HwndSourceParameters("CraneWifiTraySink")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = -100,
            PositionY = -100
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        CreateTrayIcon(tooltip);
    }

    private void CreateTrayIcon(string tooltip)
    {
        // IDI_SHIELD = 32518 or IDI_APPLICATION = 32512
        IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32518);
        if (hIcon == IntPtr.Zero)
        {
            hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512);
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = _id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = lParam.ToInt32();
            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                DoubleClickAction?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_contextMenu != null)
        {
            SetForegroundWindow(_hwndSource.Handle);
            _contextMenu.IsOpen = true;
        }
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = _id
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isCreated = false;
        }

        _hwndSource.Dispose();
        GC.SuppressFinalize(this);
    }
}

