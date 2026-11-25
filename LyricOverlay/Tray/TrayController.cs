using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LyricOverlay.Rendering;

namespace LyricOverlay.Tray;

/// <summary>Quick actions the tray keeps; every setting lives in the studio.</summary>
internal sealed class TrayCallbacks
{
    public required Action ToggleVisibility { get; init; }
    public required Func<bool> Hidden { get; init; }
    public required Action<bool> SetPaused { get; init; }
    public required Func<bool> Paused { get; init; }
    public required Action OpenStudio { get; init; }
    public required Action Quit { get; init; }
}

/// <summary>Tray icon and its short menu: open the studio, show/hide lyrics, pause, quit.</summary>
internal sealed class TrayController : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly IntPtr _iconHandle;
    private readonly TrayCallbacks _callbacks;
    private TrayMenuWindow? _menu;

    public TrayController(TrayCallbacks callbacks)
    {
        _callbacks = callbacks;
        (_icon, _iconHandle) = BuildIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Lyric Overlay — double-click to open the studio",
            Visible = true,
        };

        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowMenu();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => callbacks.OpenStudio();
    }

    /// <summary>Shows a freshly built menu, so every check, value and slider reads current state.</summary>
    private void ShowMenu()
    {
        if (_menu is { IsLoaded: true })
        {
            _menu.Activate();
            return;
        }

        var menu = new TrayMenuWindow(_callbacks) { Opacity = 0 };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
            {
                _menu = null;
            }
        };

        _menu = menu;
        menu.Show();
    }

    public void Dispose()
    {
        _menu?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }

        _icon.Dispose();
    }

    /// <summary>The official mark, embedded in the assembly, drawn at the tray's icon size.</summary>
    private static (Icon icon, IntPtr handle) BuildIcon()
    {
        var handle = IntPtr.Zero;

        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource?.Stream is { } stream)
            {
                using var loaded = new Icon(stream);
                var sized = new Icon(loaded, SystemInformation.SmallIconSize);
                return (sized, handle);
            }
        }
        catch (Exception)
        {
            // Fall through to the shell icon below rather than losing the tray icon entirely.
        }

        if (Environment.ProcessPath is { } path)
        {
            var shell = Icon.ExtractAssociatedIcon(path);
            if (shell is not null)
            {
                return (shell, handle);
            }
        }

        handle = SystemIcons.Application.Handle;
        return ((Icon)Icon.FromHandle(handle).Clone(), IntPtr.Zero);
    }
}
