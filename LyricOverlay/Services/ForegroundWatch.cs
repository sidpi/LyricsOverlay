using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LyricOverlay.Services;

/// <summary>
/// Watches foreground-window switches and size changes through Win32 event hooks, so the
/// overlay reacts to apps and fullscreen video without polling the window stack.
/// </summary>
internal sealed class ForegroundWatch : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutOfContext = 0x0000;
    private const uint GaRoot = 2;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    // Hold the delegate: the unmanaged hook keeps only this pointer, not a managed reference.
    private readonly WinEventProc _callback;
    private readonly Dispatcher _dispatcher;
    private readonly Action _changed;

    // Size/move events arrive in bursts (drag, fullscreen animation); settle before evaluating.
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private IntPtr _foregroundHook;
    private IntPtr _sizeHook;

    public ForegroundWatch(Dispatcher dispatcher, Action changed)
    {
        _dispatcher = dispatcher;
        _changed = changed;
        _callback = OnEvent;

        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            _changed();
        };

        // Own-process windows are watched too: opening the studio is exactly the focus change
        // that must fade the desktop-only overlay out. The overlay never activates itself.
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero,
            _callback, 0, 0, WineventOutOfContext);
        _sizeHook = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero,
            _callback, 0, 0, WineventOutOfContext);
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint thread, uint time)
    {
        if (_dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (eventType == EventSystemForeground)
        {
            Post(() =>
            {
                _settle.Stop();
                _changed();
            });
            return;
        }

        // Only the focused window matters: that is how borderless games and fullscreen video
        // change shape. Filtering here keeps unrelated window chatter off the UI thread.
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || Root(hwnd) != Root(foreground))
        {
            return;
        }

        Post(() =>
        {
            _settle.Stop();
            _settle.Start();
        });
    }

    private void Post(Action action)
    {
        try
        {
            _dispatcher.InvokeAsync(action);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is shutting down; nothing left to update.
        }
    }

    private static IntPtr Root(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public void Dispose()
    {
        _settle.Stop();

        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        if (_sizeHook != IntPtr.Zero)
        {
            UnhookWinEvent(_sizeHook);
            _sizeHook = IntPtr.Zero;
        }
    }
}