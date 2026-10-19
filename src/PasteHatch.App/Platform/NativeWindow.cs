using System;
using System.Drawing;
using System.Runtime.InteropServices;
using WinRT.Interop;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace PasteHatch.Platform;

/// <summary>
/// Small Win32 helpers for the main window: its HWND, DPI scale, visibility, the always-on-top
/// flag and the monitor working area used to place the floating window. Implemented with
/// <c>user32</c> calls so it can be used before WinForms is touched and to keep monitor-aware
/// coordinates in physical pixels.
/// </summary>
public static class NativeWindow
{
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);

    /// <summary>Returns the native window handle behind a WinUI 3 window.</summary>
    public static IntPtr GetHandle(WinUIWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return WindowNative.GetWindowHandle(window);
    }

    /// <summary>Effective DPI scale of the window's monitor (1.0 = 96 DPI).</summary>
    public static double GetDpiScale(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return 1.0;
        }

        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi <= 0 ? 1.0 : dpi / 96.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    public static bool IsVisible(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindowVisible(hwnd);

    /// <summary>
    /// True when the window exists but is minimized. A minimized window is still "visible" in
    /// Win32 terms, so callers must check this before treating it as a window the user can see.
    /// </summary>
    public static bool IsMinimized(IntPtr hwnd) => hwnd != IntPtr.Zero && IsIconic(hwnd);

    /// <summary>
    /// Shows a window without activating it. Used by the UI smoke tests together with an
    /// off-screen position so automated checks never steal focus or flash a window.
    /// </summary>
    public static void ShowWithoutActivation(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE
        }
    }

    /// <summary>Removes the window from the taskbar and Alt+Tab (UI smoke-test isolation).</summary>
    public static void HideFromTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const int gwlExStyle = -20;
        const long wsExToolWindow = 0x00000080L;
        const long wsExAppWindow = 0x00040000L;

        try
        {
            var style = IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, gwlExStyle)
                : new IntPtr(GetWindowLong32(hwnd, gwlExStyle));
            var updated = new IntPtr((style.ToInt64() | wsExToolWindow) & ~wsExAppWindow);
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hwnd, gwlExStyle, updated);
            }
            else
            {
                SetWindowLong32(hwnd, gwlExStyle, updated.ToInt32());
            }
        }
        catch (Exception)
        {
            // Isolation is best-effort; an unhidden taskbar button only affects test runs.
        }
    }

    /// <summary>
    /// Brings an already visible window to the foreground. <c>Window.Activate()</c> alone does
    /// not reliably raise a WinUI 3 window that is behind other windows
    /// (microsoft/microsoft-ui-xaml#7595), so the native call is made explicitly as well.
    /// The window's shown/maximized state is left to <c>AppWindow.Show()</c> and the presenter;
    /// restoring here would un-maximize a maximized window.
    /// </summary>
    /// <returns><c>true</c> when the window became the foreground window.</returns>
    public static bool BringToForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (SetForegroundWindow(hwnd))
            {
                return true;
            }

            // Windows can refuse the foreground change when the click was handled by the shell.
            // Raise the window above its neighbours anyway so the user can see it; activating it
            // is then one click away instead of the window staying hidden behind another app.
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Toggles the Windows immersive dark-mode title bar (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // The DWM attribute is cosmetic; ignore platforms where it is unavailable.
        }
    }

    /// <summary>Adds or removes the always-on-top (topmost) window style.</summary>
    public static void SetAlwaysOnTop(IntPtr hwnd, bool alwaysOnTop)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // HWND_TOPMOST / HWND_NOTOPMOST with no move, resize or activation: only the z-order changes.
        SetWindowPos(
            hwnd,
            alwaysOnTop ? HwndTopMost : HwndNoTopMost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>
    /// Returns the working area (screen minus taskbar) of the monitor that hosts
    /// <paramref name="hwnd"/>, or the primary monitor's working area for a zero handle.
    /// </summary>
    public static Rectangle GetWorkingArea(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            monitor = MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
        }

        if (monitor == IntPtr.Zero)
        {
            return Rectangle.Empty;
        }

        var monitorInfo = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return Rectangle.Empty;
        }

        var work = monitorInfo.RcWork;
        return Rectangle.FromLTRB(work.Left, work.Top, work.Right, work.Bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int newValue);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int CbSize;
        public NativeRect RcMonitor;
        public NativeRect RcWork;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
