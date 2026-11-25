using System;
using System.Runtime.InteropServices;

namespace LyricOverlay.Native;

internal static class Win32
{
    public const int GWL_EXSTYLE = -20;

    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint VK_Q = 0x51;
    public const uint VK_L = 0x4C;
    public const uint VK_S = 0x53;
    public const uint VK_UP = 0x26;
    public const uint VK_DOWN = 0x28;
    public const uint VK_OEM_PLUS = 0xBB;
    public const uint VK_OEM_MINUS = 0xBD;

    private static readonly IntPtr HwndBottom = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y,
        int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Makes the window click-through, non-activating and absent from Alt+Tab.</summary>
    public static void MakeClickThrough(IntPtr handle)
    {
        var style = GetWindowLong(handle, GWL_EXSTYLE);
        style |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(handle, GWL_EXSTYLE, style);
    }

    /// <summary>
    /// Sinks the overlay to the desktop layer: above the wallpaper/icons and below normal
    /// application windows, so shell flyouts (which are topmost) draw cleanly on top of lyrics.
    /// </summary>
    public static void SendToDesktopLayer(IntPtr handle)
    {
        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }
}
