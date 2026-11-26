using System;
using System.Windows;
using System.Windows.Controls;

namespace LyricOverlay.Updates;

/// <summary>
/// Local preview of every update state (<c>LyricOverlay.exe --update-preview</c>). The mock
/// service never touches GitHub, the network or the installed app.
/// </summary>
public partial class UpdatePreviewWindow : Window
{
    private readonly UpdateController _controller = new(new MockUpdateService());

    public UpdatePreviewWindow()
    {
        InitializeComponent();

        Panel.Attach(_controller);
        Loaded += (_, _) => Panel.PlayEnter();
        Closed += (_, _) => Panel.Attach(null);
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<UpdatePhase>(tag, out var phase))
        {
            _controller.Preview(phase);
        }
    }

    private async void OnRunFlow(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log.Info("preview: full mock flow");
        _controller.Preview(UpdatePhase.Available);
        await _controller.StartUpdateAsync();
    }

    private void OnReplay(object sender, RoutedEventArgs e) => Panel.PlayEnter();
}
