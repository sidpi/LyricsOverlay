using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LyricOverlay.Updates;

/// <summary>
/// The update card: same states, copy and motion as the studio's panel, rendered in WPF so it
/// can sit above the WebView2 studio. Data comes from <see cref="UpdateController"/>.
/// </summary>
public partial class UpdatePanelView : UserControl
{
    private static readonly TimeSpan BodyFade = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan EnterFade = TimeSpan.FromMilliseconds(220);

    private readonly IReadOnlyDictionary<UpdatePhase, FrameworkElement> _panels;
    private UpdateController? _controller;
    private UpdatePhase? _shownPhase;

    public UpdatePanelView()
    {
        InitializeComponent();

        _panels = new Dictionary<UpdatePhase, FrameworkElement>
        {
            [UpdatePhase.Idle] = IdlePanel,
            [UpdatePhase.Checking] = CheckingPanel,
            [UpdatePhase.UpToDate] = UpToDatePanel,
            [UpdatePhase.Available] = AvailablePanel,
            [UpdatePhase.Downloading] = DownloadingPanel,
            [UpdatePhase.Installing] = InstallingPanel,
            [UpdatePhase.Restarting] = RestartingPanel,
            [UpdatePhase.Error] = ErrorPanel,
        };

        CheckButton.Click += (_, _) => _ = _controller?.CheckAsync();
        UpToDateCheckButton.Click += (_, _) => _ = _controller?.CheckAsync();
        UpdateNowButton.Click += (_, _) => _ = _controller?.StartUpdateAsync();
        LaterButton.Click += (_, _) => Dismiss();
        CancelButton.Click += (_, _) => _controller?.CancelDownload();
        RetryButton.Click += (_, _) => _ = _controller?.RetryAsync();
        ErrorDismissButton.Click += (_, _) => Dismiss();
        CloseButton.Click += (_, _) => Dismiss();

        Render(animate: false, force: true);
    }

    /// <summary>The panel should close (dismiss button, Escape, or the automatic up-to-date hold).</summary>
    public event EventHandler? CloseRequested;

    public void Attach(UpdateController? controller)
    {
        if (_controller is not null)
        {
            _controller.PropertyChanged -= OnControllerChanged;
        }

        _controller = controller;

        if (_controller is not null)
        {
            _controller.PropertyChanged += OnControllerChanged;
        }

        _shownPhase = null;
        Render(animate: false, force: true);
    }

    private void Dismiss()
    {
        _controller?.Dismiss();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e) => Render(animate: true);

    private void Render(bool animate, bool force = false)
    {
        var controller = _controller;
        if (controller is null)
        {
            return;
        }

        var phase = controller.Phase;
        var info = controller.Info;
        var progress = controller.Progress;

        Title.Text = phase switch
        {
            UpdatePhase.Checking => "Checking for updates",
            UpdatePhase.UpToDate => "You're up to date",
            UpdatePhase.Available => $"Version {info?.Version ?? ""} is available",
            UpdatePhase.Downloading => "Downloading update",
            UpdatePhase.Installing => "Installing update",
            UpdatePhase.Restarting => "Restarting",
            UpdatePhase.Error => "Update failed",
            _ => "Software update",
        };

        var subtitle = phase switch
        {
            UpdatePhase.Checking => "github.com/sidpi/LyricsOverlay · latest release",
            UpdatePhase.Available => $"v{controller.CurrentVersion} installed",
            UpdatePhase.Downloading => "Signed NSIS installer",
            UpdatePhase.Installing => "Verifying signature",
            UpdatePhase.UpToDate => "No newer release was published",
            _ => null,
        };
        Subtitle.Text = subtitle ?? string.Empty;
        Subtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;

        CloseButton.IsEnabled = !controller.IsLocked;

        if (force || _shownPhase != phase)
        {
            _shownPhase = phase;
            foreach (var entry in _panels)
            {
                entry.Value.Visibility = entry.Key == phase ? Visibility.Visible : Visibility.Collapsed;
            }

            StartLoops(phase);
            if (animate)
            {
                AnimateBody();
            }
        }

        FromChip.Text = $"v{controller.CurrentVersion}";
        ToChip.Text = $"v{info?.Version ?? "?"}";
        PublishedText.Text = ReleaseDate(info?.PublishedAt) ?? string.Empty;

        if (NotesText.Text != (info?.Notes ?? DefaultNotes))
        {
            NotesText.Text = info?.Notes ?? DefaultNotes;
        }

        switch (phase)
        {
            case UpdatePhase.UpToDate:
                UpToDateText.Text = controller.CurrentVersion.Length > 0
                    ? $"You're on the latest version (v{controller.CurrentVersion})"
                    : "You're on the latest version";
                break;

            case UpdatePhase.Downloading:
            {
                var percent = Math.Min(100d, progress.Percent);
                PercentText.Text = Math.Floor(percent).ToString("0", CultureInfo.CurrentCulture);
                SizeText.Text = progress.Total > 0
                    ? $"{Megabytes(progress.Downloaded)} MB / {Megabytes(progress.Total)} MB"
                    : $"{Megabytes(progress.Downloaded)} MB";
                SpeedText.Text = Speed(progress.BytesPerSecond);
                AnimateBar(BarFillScale, percent);
                break;
            }

            case UpdatePhase.Restarting:
                RestartingText.Text = info is null
                    ? "LyricsOverlay reopens on the new build in a moment."
                    : $"LyricsOverlay reopens on v{info.Version} in a moment.";
                break;

            case UpdatePhase.Error:
                ErrorText.Text = controller.ErrorMessage ?? "Something went wrong.";
                var hint = Hint(controller.ErrorKind);
                ErrorHint.Text = hint ?? string.Empty;
                ErrorHint.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
                break;
        }
    }

    private const string DefaultNotes = "No release notes were published for this build.";

    private static string? Hint(UpdateErrorKind? kind) => kind switch
    {
        UpdateErrorKind.Offline => "LyricsOverlay keeps running while you're offline.",
        UpdateErrorKind.NotFound =>
            "The release needs a LyricsOverlay-Setup-*.exe asset published for Windows.",
        UpdateErrorKind.Network => "Nothing was installed — retrying starts a fresh download.",
        UpdateErrorKind.Verification => "Only installers that match the published checksum are accepted.",
        UpdateErrorKind.Install => "You can also run the downloaded setup file manually.",
        _ => null,
    };

    private static string Megabytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.CurrentCulture);

    private static string Speed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{Megabytes((long)bytesPerSecond)} MB/s";
        }

        return bytesPerSecond > 0
            ? $"{Math.Max(1, (int)Math.Round(bytesPerSecond / 1024))} KB/s"
            : "—";
    }

    private static string? ReleaseDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);

    private void StartLoops(UpdatePhase phase)
    {
        SpinnerRotate.BeginAnimation(
            RotateTransform.AngleProperty,
            phase == UpdatePhase.Checking
                ? new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(950))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                }
                : null);

        BarShimmerShift.BeginAnimation(
            TranslateTransform.XProperty,
            phase == UpdatePhase.Downloading ? Shimmer() : null);

        InstallShimmerShift.BeginAnimation(
            TranslateTransform.XProperty,
            phase == UpdatePhase.Installing ? Shimmer() : null);

        PulseDot.BeginAnimation(
            OpacityProperty,
            phase == UpdatePhase.Installing
                ? new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(800))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                }
                : null);
    }

    private static DoubleAnimation Shimmer() => new(-160, 400, TimeSpan.FromMilliseconds(1500))
    {
        RepeatBehavior = RepeatBehavior.Forever,
    };

    private static void AnimateBar(ScaleTransform scale, double percent)
    {
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(percent / 100d, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    private void AnimateBody()
    {
        var shift = new TranslateTransform(0, 6);
        BodyHost.RenderTransform = shift;
        BodyHost.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BodyHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, BodyFade) { EasingFunction = ease });
        shift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    /// <summary>Slide-and-fade in, anchored to the card's bottom-right corner.</summary>
    public void PlayEnter()
    {
        Card.Opacity = 0;
        EnterShift.Y = 18;
        EnterScale.ScaleX = 0.97;
        EnterScale.ScaleY = 0.97;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, EnterFade) { EasingFunction = ease });
        EnterShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        EnterScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        EnterScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    /// <summary>Fade-and-drop out; <paramref name="done"/> runs when it is off screen.</summary>
    public void PlayExit(Action done)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        fade.Completed += (_, _) => done();

        Card.BeginAnimation(OpacityProperty, fade);
        EnterShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(10, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        EnterScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.98, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        EnterScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.98, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }
}
