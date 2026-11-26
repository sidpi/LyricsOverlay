using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LyricOverlay.Updates;

/// <summary>
/// Floating update card, anchored to the studio's bottom-right corner. A separate top-level
/// window is required: the studio's WebView2 surface is an HWND child, so a WPF sibling
/// element could not paint above it.
/// </summary>
public partial class UpdatePanelWindow : Window
{
    /// <summary>Transparent border kept around the card so its drop shadow is not clipped.</summary>
    private const double CardInset = 30;

    private readonly UpdateController _controller;
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(4.2) };
    private bool _closing;

    public UpdatePanelWindow(UpdateController controller, Window? anchor)
    {
        InitializeComponent();

        _controller = controller;
        Panel.Attach(controller);
        Panel.CloseRequested += (_, _) => ClosePanel();
        _controller.PhaseChanged += OnPhaseChanged;
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            ClosePanel();
        };

        Loaded += (_, _) =>
        {
            AnchorTo(anchor);
            Panel.PlayEnter();
        };
        SizeChanged += (_, _) => AnchorTo(anchor);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ClosePanel();
            }
        };
        Closed += (_, _) =>
        {
            _autoClose.Stop();
            _controller.PhaseChanged -= OnPhaseChanged;
            Panel.Attach(null);
        };
    }

    private void OnPhaseChanged(object? sender, UpdatePhase phase)
    {
        _autoClose.Stop();
        if (phase == UpdatePhase.UpToDate)
        {
            _autoClose.Start();
        }
    }

    /// <summary>Bottom-right of the studio when it is on screen, otherwise of the work area.</summary>
    public void AnchorTo(Window? anchor)
    {
        var work = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 320;

        // Clamp to the whole virtual desktop: the studio may live on another monitor.
        var minLeft = SystemParameters.VirtualScreenLeft + 8;
        var minTop = SystemParameters.VirtualScreenTop + 8;
        var maxRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 8;
        var maxBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 8;

        // The card's visible edge (inside the transparent shadow inset) sits 20 px from the corner.
        var left = work.Right - width + CardInset - 20;
        var top = work.Bottom - height + CardInset - 20;

        if (anchor is { IsVisible: true })
        {
            left = anchor.Left + anchor.ActualWidth - width + CardInset - 20;
            top = anchor.Top + anchor.ActualHeight - height + CardInset - 20;
        }

        Left = Math.Max(minLeft, Math.Min(left, maxRight - width));
        Top = Math.Max(minTop, Math.Min(top, maxBottom - height));
    }

    public void ClosePanel()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _autoClose.Stop();
        Panel.PlayExit(Close);
    }
}
