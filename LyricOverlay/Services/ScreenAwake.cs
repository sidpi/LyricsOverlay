using System;
using System.Runtime.InteropServices;

namespace LyricOverlay.Services;

/// <summary>Holds the display awake while a track is playing and the setting is enabled.</summary>
internal static class ScreenAwake
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);

    private static bool _active;

    /// <summary>Requests or releases the wake lock; safe to call repeatedly.</summary>
    public static void Set(bool active)
    {
        if (active == _active)
        {
            return;
        }

        try
        {
            SetThreadExecutionState(active
                ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
                : ES_CONTINUOUS);
            _active = active;
        }
        catch
        {
            // Not fatal: the overlay keeps working without the wake lock.
        }
    }

    public static void Release() => Set(false);
}
