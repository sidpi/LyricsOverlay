using System;
using System.Windows;
using System.Windows.Controls;

using WpfBrush = System.Windows.Media.Brush;

namespace LyricOverlay.Tray;

/// <summary>
/// The tray popup: only the quick actions that belong outside the studio — open the studio,
/// show/hide the lyrics, pause tracking and quit. Everything else lives in the studio.
/// </summary>
internal partial class TrayMenuView : UserControl
{
    private readonly TrayCallbacks _callbacks;

    public TrayMenuView(TrayCallbacks callbacks)
    {
        _callbacks = callbacks;
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var root = MenuRoot;

        AddHeader(root, "STUDIO");
        root.Children.Add(Row("Open LyricsOverlay…", null, null, () =>
        {
            _callbacks.OpenStudio();
            CloseMenu();
        }));
        root.Children.Add(Row("Check for updates…", null, null, () =>
        {
            _callbacks.CheckForUpdates();
            CloseMenu();
        }));

        AddDivider(root);
        AddHeader(root, "PLAYBACK");
        root.Children.Add(Row(
            _callbacks.Hidden() ? "Show overlay" : "Hide overlay",
            null,
            Hint("Ctrl+Alt+L"),
            () =>
            {
                _callbacks.ToggleVisibility();
                CloseMenu();
            }));

        AddToggle(root, "Pause tracking", _callbacks.Paused(), _callbacks.SetPaused);

        AddDivider(root);
        root.Children.Add(Row("Quit", null, Hint("Ctrl+Alt+Q"), () =>
        {
            _callbacks.Quit();
            CloseMenu();
        }));
    }

    private void AddHeader(Panel root, string text) => root.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = Palette("MutedText"),
        Margin = new Thickness(10, 10, 10, 3),
    });

    private void AddDivider(Panel root) => root.Children.Add(new Border
    {
        Height = 1,
        Background = Palette("Divider"),
        Margin = new Thickness(10, 5, 10, 5),
    });

    private void AddToggle(Panel root, string text, bool isChecked, Action<bool> set)
    {
        var check = Check(isChecked);
        var row = Row(text, check, null, null);
        row.Click += (_, _) =>
        {
            var next = check.Visibility != Visibility.Visible;
            check.Visibility = next ? Visibility.Visible : Visibility.Collapsed;
            set(next);
        };
        root.Children.Add(row);
    }

    private Button Row(string text, TextBlock? check, UIElement? trailing, Action? onClick,
        double indent = 0, double height = 30)
    {
        var grid = new Grid { Margin = new Thickness(10 + indent, 0, 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (check is not null)
        {
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);
        }

        var label = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = Palette("RowText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        var button = new Button
        {
            Style = (Style)FindResource("RowButton"),
            Content = grid,
            Height = height,
        };
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    private TextBlock Check(bool visible, double size = 11) => new()
    {
        Text = "✓",
        FontSize = size,
        Foreground = Palette("RowText"),
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(1, 0, 0, 0),
    };

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 10.5,
        Foreground = Palette("MutedText"),
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void CloseMenu() => Window.GetWindow(this)?.Close();

    private WpfBrush Palette(string key) => (WpfBrush)FindResource(key);
}
