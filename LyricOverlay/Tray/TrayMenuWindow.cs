using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LyricOverlay.Tray;

/// <summary>
/// Borderless, translucent popup that hosts the tray menu above the notification area.
/// A fresh window is built for every opening, so the menu always shows current settings.
/// </summary>
internal sealed class TrayMenuWindow : Window
{
    public TrayMenuWindow(TrayCallbacks callbacks)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 312;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = new TrayMenuView(callbacks);

        Loaded += (_, _) =>
        {
            AnchorToCorner();
            Opacity = 1;
        };
        SizeChanged += (_, _) => AnchorToCorner();
        Deactivated += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>Keeps the menu's bottom-right corner pinned near the tray as sections expand.</summary>
    public void AnchorToCorner()
    {
        if (!IsLoaded)
        {
            return;
        }

        var work = SystemParameters.WorkArea;
        Left = Math.Max(work.Left + 8, work.Right - ActualWidth - 8);
        Top = Math.Max(work.Top + 8, work.Bottom - ActualHeight - 8);
    }
}
