using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LyricOverlay.Services;

/// <summary>
/// Answers "is the desktop clear right now?" — no application focused and nothing sitting over
/// the overlay's area — so lyrics are only painted on an idle desktop. Windows shell surfaces
/// (taskbar, tray overflow, Quick Settings, notifications, search, start) count as idle desktop.
/// </summary>
internal static class DesktopGuard
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const uint GaRoot = 2;
    private const int DwmwaCloaked = 14;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int QunsD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name,
        ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Window classes that belong to the Windows shell rather than to an app.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Desktop and taskbar.
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        // Tray overflow and XAML island popups.
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        // Notification centre / Quick Settings hosts.
        "ControlCenter",
        "ControlCenterWindow",
    };

    /// <summary>Processes that only ever present Windows shell surfaces.</summary>
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShellExperienceHost",
        "SearchHost",
        "SearchApp",
        "SearchUI",
        "StartMenuExperienceHost",
        "TextInputHost",
    };

    /// <summary>True when the desktop has focus and no application window covers the overlay.</summary>
    public static bool IsDesktopClear(IntPtr overlay)
    {
        // Focus must be on the desktop/shell, never on an app (including our own studio). Shell
        // flyouts can focus a child window, so classify the root window as well.
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var root = GetAncestor(foreground, GaRoot);
            if (root == IntPtr.Zero)
            {
                root = foreground;
            }

            if (!IsShellWindow(root) && !IsShellWindow(foreground))
            {
                return false;
            }
        }

        if (!GetWindowRect(overlay, out var bounds))
        {
            return true;
        }

        var clear = true;
        EnumWindows((hwnd, _) =>
        {
            if (!clear)
            {
                return false;
            }

            if (hwnd == overlay)
            {
                return true;
            }

            if (Blocks(hwnd, bounds))
            {
                clear = false;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return clear;
    }

    /// <summary>
    /// True when a fullscreen game, video or presentation covers the monitor LyricsOverlay is on.
    /// Combines the shell's exclusive-fullscreen state with a foreground-bounds check, so
    /// borderless games and fullscreen browser video are caught as well.
    /// </summary>
    public static bool IsFullscreenCovering(IntPtr overlay)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        // Our own windows, the desktop shell and the taskbar are never "a fullscreen app".
        var root = Root(foreground);
        if (root == overlay || foreground == overlay || IsOwnedByCurrentProcess(foreground))
        {
            return false;
        }

        if (IsShellWindow(root) || IsShellWindow(foreground))
        {
            return false;
        }

        if (!IsWindowVisible(foreground) || IsIconic(foreground) || IsCloaked(foreground))
        {
            return false;
        }

        // Multi-monitor: only the monitor LyricsOverlay sits on can hide it.
        var overlayMonitor = MonitorFromWindow(overlay, MonitorDefaultToNearest);
        var foregroundMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (overlayMonitor == IntPtr.Zero || foregroundMonitor != overlayMonitor)
        {
            return false;
        }

        // Exclusive DirectX/Vulkan fullscreen and presentation modes are fullscreen by definition.
        if (SHQueryUserNotificationState(out var state) == 0 &&
            state is QunsD3dFullScreen or QunsPresentationMode)
        {
            return true;
        }

        // Everything else — including QUNS_BUSY, which is also reported for presentation settings
        // and other shell states — only counts when the focused window really covers its monitor.
        // This is what catches borderless games and fullscreen browser video.
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(foregroundMonitor, ref info) || !GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        const int tolerance = 2;
        return rect.Left <= info.RcMonitor.Left + tolerance &&
               rect.Top <= info.RcMonitor.Top + tolerance &&
               rect.Right >= info.RcMonitor.Right - tolerance &&
               rect.Bottom >= info.RcMonitor.Bottom - tolerance;
    }

    private static IntPtr Root(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private static bool IsOwnedByCurrentProcess(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        return processId != 0 && processId == (uint)Environment.ProcessId;
    }

    private static bool Blocks(IntPtr hwnd, Rect bounds)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        // Only top-level windows matter; owned popups come along with their parent.
        if (GetAncestor(hwnd, GaRoot) != hwnd)
        {
            return false;
        }

        if (IsShellWindow(hwnd) || IsCloaked(hwnd))
        {
            return false;
        }

        // Alt+Tab-invisible tool windows (tray flyouts, our own helpers) are not "apps".
        if ((GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0)
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        if (rect.Right - rect.Left <= 1 || rect.Bottom - rect.Top <= 1)
        {
            return false;
        }

        return rect.Left < bounds.Right && rect.Right > bounds.Left &&
               rect.Top < bounds.Bottom && rect.Bottom > bounds.Top;
    }

    private static bool IsShellWindow(IntPtr hwnd)
    {
        var cls = ClassName(hwnd);
        if (cls.Length == 0)
        {
            return false;
        }

        if (ShellClasses.Contains(cls))
        {
            return true;
        }

        // These classes are shared with ordinary UWP apps, so trust the process instead of the
        // class alone: only shell hosts may be ignored.
        if (cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("Xaml", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("ControlCenter", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("Overflow", StringComparison.OrdinalIgnoreCase))
        {
            var process = ProcessName(hwnd);
            if (process is null)
            {
                return false;
            }

            if (ShellProcesses.Contains(process))
            {
                return true;
            }

            // Explorer hosts its own XAML island flyouts (notification centre, overflow, etc.)
            // but also hosts normal folder windows (`CabinetWClass`), which stay applications.
            return string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(cls, "CabinetWClass", StringComparison.OrdinalIgnoreCase) &&
                   (cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                    cls.Contains("Xaml", StringComparison.OrdinalIgnoreCase) ||
                    cls.Contains("ControlCenter", StringComparison.OrdinalIgnoreCase) ||
                    cls.Contains("Overflow", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    private static string? ProcessName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var name = new StringBuilder(1024);
            var size = (uint)name.Capacity;
            if (!QueryFullProcessImageName(process, 0, name, ref size))
            {
                return null;
            }

            return Path.GetFileNameWithoutExtension(name.ToString(0, (int)size));
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
}
