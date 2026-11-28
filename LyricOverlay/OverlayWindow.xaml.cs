using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Text.Json;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using LyricOverlay.Diagnostics;
using LyricOverlay.Lyrics;
using LyricOverlay.Native;
using LyricOverlay.Rendering;
using LyricOverlay.Services;
using LyricOverlay.Tray;
using LyricOverlay.Updates;

namespace LyricOverlay;

public partial class OverlayWindow : Window
{
    private const int HotkeyQuit = 0x1001;
    private const int HotkeyToggle = 0x1002;
    private const int HotkeyUp = 0x1003;
    private const int HotkeyDown = 0x1004;
    private const int HotkeyScaleUp = 0x1005;
    private const int HotkeyScaleDown = 0x1006;
    private const int HotkeyStyle = 0x1007;

    private readonly OverlayStyle _style = OverlayStyle.Load();
    private readonly LyricsProvider _lyrics = new();
    private StyleKind _styleKind;

    private readonly DispatcherTimer _poller = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _animator = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private PlaybackSnapshot? _snapshot;
    private LyricsResult? _lyricsResult;
    private string? _loadedTrackKey;
    private bool _fetching;
    private bool _hidden;
    private bool _paused;

    // Playback clock: re-anchored on every poll, advanced locally so animation is smooth.
    private double _anchorMs;
    private DateTime _anchorAt = DateTime.UtcNow;
    private bool _playing;
    private double _durationMs;
    private double _offsetMs;

    // Line state.
    private int _lineIndex = -1;
    private DateTime _lineChangedAt = DateTime.UtcNow;
    private string? _previousLine;
    private string _lineText = string.Empty;
    private double _lineStartMs;
    private double _lineEndMs;
    private IReadOnlyList<TimedWord> _words = Array.Empty<TimedWord>();
    private readonly List<string> _history = new();
    private readonly List<string> _upcoming = new();


    private SKTypeface? _regular;
    private SKTypeface? _bold;
    private SKTypeface? _mono;
    private TrayController? _tray;
    private ControlServer? _control;
    private StudioWindow? _studio;
    private readonly UpdateController _updates;
    private UpdatePanelWindow? _updatePanel;

    // Screen state: desktop focus (desktopOnly) and fullscreen apps (auto-hide).
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private ForegroundWatch? _foregroundWatch;
    private bool _desktopSuppressed;
    private bool _fullscreenSuppressed;
    private double _fadeTarget = 1;

    public OverlayWindow()
    {
        InitializeComponent();

        _styleKind = StyleCatalog.Parse(_style.Style);
        if (StyleCatalog.IsFullScreenOnly(_styleKind) && !_style.FullScreen)
        {
            // A full-screen-only style cannot run in the compact bar; start in the default.
            _styleKind = StyleKind.Kinetic;
            _style.Style = StyleCatalog.NameOf(_styleKind);
            _style.Save();
        }

        _poller.Tick += async (_, _) => await PollAsync();
        _animator.Tick += (_, _) => OnAnimateFrame();
        _fade.Tick += (_, _) => OnFadeFrame();
        _poller.Start();
        // The animator starts on the first playing track (see SyncAnimator); idling or hiding the
        // overlay must not repaint the surface or wake the UI thread 30 times a second.

        // Mock mode (LYRICOVERLAY_UPDATE_MOCK=1) exercises the panel without a published release.
        _updates = new UpdateController(UpdateServices.Create(), QuitApp);

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _poller.Stop();
            _animator.Stop();
            _fade.Stop();
            _foregroundWatch?.Dispose();
            _foregroundWatch = null;
            ScreenAwake.Release();
            _studio?.Close();
            _tray?.Dispose();
            _control?.Dispose();
        };
    }

    private double PositionMs
    {
        get
        {
            if (_paused)
            {
                return _anchorMs + _offsetMs;
            }

            return _anchorMs + _offsetMs + (_playing ? (DateTime.UtcNow - _anchorAt).TotalMilliseconds : 0);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Saved source applies before the first lookup.
        _lyrics.SetSource(LyricsSources.Parse(_style.LyricsProvider));

        ApplyWindowLayout();

        var handle = new WindowInteropHelper(this).Handle;
        Win32.MakeClickThrough(handle);
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        RegisterHotkeys(handle);

        // Focus and fullscreen changes arrive through Win32 hooks; evaluate once for the first paint.
        ApplyZOrder();
        SyncWatcher();
        EvaluateScreenState();

        _tray = new TrayController(new TrayCallbacks
        {
            ToggleVisibility = ToggleVisibility,
            Hidden = () => _hidden,
            SetPaused = SetPaused,
            Paused = () => _paused,
            OpenStudio = OpenStudio,
            CheckForUpdates = CheckForUpdates,
            DisplayMode = () => _style.DisplayMode,
            SetDisplayMode = SetDisplayMode,
            HideInFullscreen = () => _style.HideInFullscreen,
            SetHideInFullscreen = SetHideInFullscreen,
            Quit = QuitApp,
        });

        // The studio talks to this process over a named pipe; the tray keeps working either way.
        _control = new ControlServer(HandleControlRequest);
        _control.Start();

        Log.Info($"overlay started (style={StyleCatalog.NameOf(_styleKind)}, font={_style.Font}, scale={_style.TextScale:0.00}, fullscreen={_style.FullScreen}, mode={_style.DisplayMode}, autoHideFullscreen={_style.HideInFullscreen}, tray=on)");
    }

    /// <summary>Bottom-centred lyric strip, or the whole work area in full-screen mode.</summary>
    private void ApplyWindowLayout()
    {
        var work = SystemParameters.WorkArea;
        if (_style.FullScreen)
        {
            Width = work.Width;
            Height = work.Height;
            Left = work.Left;
            Top = work.Top;
            return;
        }

        Width = Math.Min(1000, work.Width - 80);
        Height = 240;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Bottom - Height - _style.BottomMargin;
    }

    private void SetFullScreen(bool enabled)
    {
        _style.FullScreen = enabled;
        _style.Save();
        ApplyWindowLayout();

        if (!enabled && StyleCatalog.IsFullScreenOnly(_styleKind))
        {
            // Leaving full screen drops back to a style that works in the compact bar.
            SetStyle(StyleKind.Kinetic);
        }

        Log.Info(enabled ? "full-screen lyrics on" : "full-screen lyrics off");
        Canvas.InvalidateVisual();
    }

    private void RegisterHotkeys(IntPtr handle)
    {
        const uint ctrlAlt = Win32.MOD_CONTROL | Win32.MOD_ALT;
        Win32.RegisterHotKey(handle, HotkeyQuit, ctrlAlt, Win32.VK_Q);
        Win32.RegisterHotKey(handle, HotkeyToggle, ctrlAlt, Win32.VK_L);
        Win32.RegisterHotKey(handle, HotkeyStyle, ctrlAlt, Win32.VK_S);
        Win32.RegisterHotKey(handle, HotkeyUp, ctrlAlt | Win32.MOD_NOREPEAT, Win32.VK_UP);
        Win32.RegisterHotKey(handle, HotkeyDown, ctrlAlt | Win32.MOD_NOREPEAT, Win32.VK_DOWN);
        Win32.RegisterHotKey(handle, HotkeyScaleUp, ctrlAlt | Win32.MOD_NOREPEAT, Win32.VK_OEM_PLUS);
        Win32.RegisterHotKey(handle, HotkeyScaleDown, ctrlAlt | Win32.MOD_NOREPEAT, Win32.VK_OEM_MINUS);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotkeyQuit:
                QuitApp();
                handled = true;
                break;
            case HotkeyToggle:
                ToggleVisibility();
                handled = true;
                break;
            case HotkeyStyle:
                SetStyle(StyleCatalog.Next(_styleKind, _style.FullScreen));
                handled = true;
                break;
            case HotkeyUp:
                if (!_style.FullScreen)
                {
                    Top = Math.Max(0, Top - 24);
                }

                handled = true;
                break;
            case HotkeyDown:
                if (!_style.FullScreen)
                {
                    Top += 24;
                }

                handled = true;
                break;
            case HotkeyScaleUp:
                AdjustScale(0.05);
                handled = true;
                break;
            case HotkeyScaleDown:
                AdjustScale(-0.05);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ToggleVisibility()
    {
        _hidden = !_hidden;
        ApplyPresentation();
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused)
        {
            _poller.Stop();
            _playing = false;
            ScreenAwake.Set(false);
        }
        else
        {
            _anchorAt = DateTime.UtcNow;
            _poller.Start();
        }

        Log.Info(paused ? "tracking paused from tray" : "tracking resumed from tray");
        Canvas.InvalidateVisual();
        SyncAnimator();
    }

    private void SetStyle(StyleKind kind)
    {
        if (StyleCatalog.IsFullScreenOnly(kind) && !_style.FullScreen)
        {
            // Full-screen-exclusive styles bring the full stage with them.
            SetFullScreen(true);
        }

        _styleKind = kind;
        _style.Style = StyleCatalog.NameOf(kind);
        _style.Save();
        Log.Info($"style -> {_style.Style}");
        Canvas.InvalidateVisual();
    }

    private void SetFont(string font)
    {
        _style.Font = font;
        _style.Save();
        _regular = null;
        _bold = null;
        Log.Info($"font -> {font}");
        Canvas.InvalidateVisual();
    }

    private void SetLyricsProvider(string name)
    {
        _style.LyricsProvider = name;
        _style.Save();
        _lyrics.SetSource(LyricsSources.Parse(name));

        // Force a re-resolve of the current track against the new source.
        _loadedTrackKey = null;
        _lyricsResult = null;
        Log.Info($"lyrics provider -> {LyricsSources.NameOf(LyricsSources.Parse(name))}");
        Canvas.InvalidateVisual();
    }

    private void NudgeTiming(int direction) => SetTiming(_offsetMs + direction * 100);

    private void SetTiming(double offsetMs)
    {
        _offsetMs = Math.Clamp(offsetMs, -3000, 3000);
        Log.Info($"timing offset -> {_offsetMs:0} ms");
        Canvas.InvalidateVisual();
    }

    private void SetMatchArtwork(bool enabled)
    {
        _style.MatchArtwork = enabled;
        _style.Save();
        Log.Info(enabled ? "artwork colour matching on" : "artwork colour matching off");

        if (!enabled)
        {
            _style.ArtworkHighlight = null;
            _style.ArtworkSecondary = null;
            _style.ArtworkGlow = null;
        }
        else if (_snapshot is not null)
        {
            _ = ApplyArtworkColorAsync(_snapshot.Key);
        }

        Canvas.InvalidateVisual();
    }

    /// <summary>Drops the artwork-derived tones and goes back to the saved manual colours.</summary>
    private void ResetColors()
    {
        _style.MatchArtwork = false;
        _style.ArtworkHighlight = null;
        _style.ArtworkSecondary = null;
        _style.ArtworkGlow = null;
        _style.Save();
        Log.Info("colours reset to manual");
        Canvas.InvalidateVisual();
    }

    private void ApplyArtworkForTrack(string trackKey)
    {
        if (!_style.MatchArtwork)
        {
            _style.ArtworkHighlight = null;
            _style.ArtworkSecondary = null;
            _style.ArtworkGlow = null;
            return;
        }

        _ = ApplyArtworkColorAsync(trackKey);
    }

    private async System.Threading.Tasks.Task ApplyArtworkColorAsync(string trackKey)
    {
        var palette = await AlbumArtColors.GetAsync(trackKey);
        if (_snapshot?.Key != trackKey)
        {
            return; // track changed while decoding
        }

        _style.ArtworkHighlight = palette?.Highlight;
        _style.ArtworkSecondary = palette?.Highlight.WithAlpha(140);
        _style.ArtworkGlow = palette?.Glow;
        if (palette is null)
        {
            Log.Info("no artwork colour for this track; keeping manual colours");
        }

        Canvas.InvalidateVisual();
    }

    private void SetKeepAwake(bool enabled)
    {
        _style.KeepAwake = enabled;
        _style.Save();
        ScreenAwake.Set(enabled && _playing && !_paused);
    }

    private void QuitApp()
    {
        Log.Info("quit requested");
        ScreenAwake.Release();
        Application.Current.Shutdown();
    }

    private static int CurrentLineIndex(LrcDocument document, double positionMs)
    {
        var index = 0;
        for (var i = 0; i < document.Lines.Count; i++)
        {
            if (document.Lines[i].Time.TotalMilliseconds > positionMs)
            {
                break;
            }

            index = i;
        }

        return index;
    }

    private void AdjustScale(double delta) => SetScale(_style.TextScale + delta);

    private void SetScale(double scale)
    {
        _style.TextScale = Math.Clamp(scale, 0.6, 2.0);
        _style.Save();
        Log.Info($"text scale -> {_style.TextScale:0.00}");
        Canvas.InvalidateVisual();
    }

    /// <summary>
    /// Opens the studio, building it on demand. Closing the studio disposes its window and
    /// WebView2; the next open gets a fresh one instead of waking a hidden browser.
    /// </summary>
    internal void OpenStudio()
    {
        if (_studio is null)
        {
            var studio = new StudioWindow(this);
            studio.Closed += (_, _) =>
            {
                if (ReferenceEquals(_studio, studio))
                {
                    _studio = null;
                }
            };
            _studio = studio;
        }

        _studio.Present();
    }

    /// <summary>Opens the update panel and asks GitHub Releases for a newer build.</summary>
    internal void CheckForUpdates()
    {
        ShowUpdatePanel();
        _ = _updates.CheckAsync();
    }

    private void ShowUpdatePanel()
    {
        if (_updatePanel is { IsVisible: true })
        {
            _updatePanel.Activate();
            return;
        }

        var panel = new UpdatePanelWindow(_updates, _studio is { IsVisible: true } ? _studio : null);
        panel.Closed += (_, _) =>
        {
            if (ReferenceEquals(_updatePanel, panel))
            {
                _updatePanel = null;
            }
        };

        _updatePanel = panel;
        panel.Show();
    }

    internal string ControlRequest(JsonElement request) => HandleControlRequest(request);

    internal object StateSnapshot() => ControlSnapshot();

    private string HandleControlRequest(JsonElement request)
    {
        var command = request.ValueKind == JsonValueKind.Object && request.TryGetProperty("cmd", out var cmd)
            ? cmd.GetString()
            : "state";

        if (string.Equals(command, "studio", StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.Invoke(OpenStudio);
            return JsonSerializer.Serialize(new { ok = true, state = ControlSnapshot() });
        }

        if (string.Equals(command, "apply", StringComparison.OrdinalIgnoreCase) &&
            request.TryGetProperty("patch", out var patch))
        {
            Dispatcher.Invoke(() => ApplyControlPatch(patch));
        }

        return JsonSerializer.Serialize(new { ok = true, state = ControlSnapshot() });
    }

    /// <summary>The live overlay settings and playback, exactly as the overlay sees them.</summary>
    private object ControlSnapshot() => new
    {
        style = StyleCatalog.NameOf(_styleKind),
        font = _style.Font,
        textScale = Math.Round(_style.TextScale, 2),
        fullScreen = _style.FullScreen,
        hidden = _hidden,
        autoStart = AutoStart.Enabled,
        offsetMs = Math.Round(_offsetMs),
        keepAwake = _style.KeepAwake,
        matchArtwork = _style.MatchArtwork,
        textColor = _style.TextColor,
        highlightColor = _style.HighlightColor,
        secondaryColor = _style.SecondaryColor,
        lyricsProvider = _style.LyricsProvider,
        displayMode = _style.DisplayMode,
        hideInFullscreen = _style.HideInFullscreen,
        playing = _playing && !_paused,
        track = _snapshot?.Title ?? string.Empty,
        artist = _snapshot?.Artist ?? string.Empty,
        positionMs = Math.Max(0, PositionMs),
        durationMs = Math.Max(0, _durationMs),
        styles = StyleCatalog.All
            .Where(entry => _style.FullScreen || !StyleCatalog.IsFullScreenOnly(entry.Kind))
            .Select(entry => new { name = entry.Name, description = entry.Description })
            .ToArray(),
        fonts = StyleCatalog.Fonts.ToArray(),
    };

    private void ApplyControlPatch(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (patch.TryGetProperty("style", out var style) && style.ValueKind == JsonValueKind.String)
        {
            SetStyle(StyleCatalog.Parse(style.GetString()));
        }

        if (patch.TryGetProperty("font", out var font) && font.ValueKind == JsonValueKind.String &&
            font.GetString() is { Length: > 0 } fontName)
        {
            SetFont(fontName);
        }

        if (patch.TryGetProperty("textScale", out var scale) && scale.TryGetDouble(out var scaleValue))
        {
            SetScale(scaleValue);
        }

        if (patch.TryGetProperty("fullScreen", out var fullScreen) &&
            fullScreen.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            SetFullScreen(fullScreen.GetBoolean());
        }

        if (patch.TryGetProperty("hidden", out var hidden) &&
            hidden.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            SetHidden(hidden.GetBoolean());
        }

        if (patch.TryGetProperty("offsetMs", out var offsetMs) && offsetMs.TryGetDouble(out var timingValue))
        {
            SetTiming(timingValue);
        }

        if (patch.TryGetProperty("autoStart", out var autoStart) &&
            autoStart.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            AutoStart.Set(autoStart.GetBoolean());
            Log.Info(autoStart.GetBoolean() ? "start with Windows on" : "start with Windows off");
        }

        if (patch.TryGetProperty("keepAwake", out var keepAwake) &&
            keepAwake.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            SetKeepAwake(keepAwake.GetBoolean());
        }

        if (patch.TryGetProperty("matchArtwork", out var matchArtwork) &&
            matchArtwork.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            SetMatchArtwork(matchArtwork.GetBoolean());
        }

        if (patch.TryGetProperty("lyricsProvider", out var provider) &&
            provider.ValueKind == JsonValueKind.String &&
            provider.GetString() is { Length: > 0 } providerName)
        {
            SetLyricsProvider(providerName);
        }

        if (patch.TryGetProperty("displayMode", out var displayMode) &&
            displayMode.ValueKind == JsonValueKind.String)
        {
            SetDisplayMode(displayMode.GetString());
        }

        if (patch.TryGetProperty("hideInFullscreen", out var hideInFullscreen) &&
            hideInFullscreen.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            SetHideInFullscreen(hideInFullscreen.GetBoolean());
        }

        var colorsChanged = false;
        colorsChanged |= ApplyColor(patch, "textColor", value => _style.TextColor = value);
        colorsChanged |= ApplyColor(patch, "highlightColor", value => _style.HighlightColor = value);
        colorsChanged |= ApplyColor(patch, "secondaryColor", value => _style.SecondaryColor = value);
        if (colorsChanged)
        {
            _style.Save();
            Log.Info("manual colours updated from studio");
            Canvas.InvalidateVisual();
        }

        if (patch.TryGetProperty("resetColors", out var reset) && reset.ValueKind == JsonValueKind.True)
        {
            ResetColors();
        }
    }

    private static bool ApplyColor(JsonElement patch, string name, Action<string> apply)
    {
        if (!patch.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        apply(text);
        return true;
    }

    private void SetHidden(bool hidden)
    {
        _hidden = hidden;
        ApplyPresentation();
        Log.Info(hidden ? "overlay hidden from studio" : "overlay shown from studio");
    }

    /// <summary>Fades the lyrics in or out for the active mode, focus and fullscreen state.</summary>
    private void ApplyPresentation()
    {
        if (!IsLoaded)
        {
            return;
        }

        var visible = !_hidden && !_desktopSuppressed && !_fullscreenSuppressed;
        if (visible)
        {
            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }

            ApplyZOrder();
            _fadeTarget = 1;
        }
        else
        {
            _fadeTarget = 0;
        }

        if (!_fade.IsEnabled && Math.Abs(Opacity - _fadeTarget) > 0.001)
        {
            _fade.Start();
        }

        SyncAnimator();
    }

    private void OnFadeFrame()
    {
        const double step = 1.0 / 14.0;
        Opacity = _fadeTarget > Opacity
            ? Math.Min(_fadeTarget, Opacity + step)
            : Math.Max(_fadeTarget, Opacity - step);

        if (Math.Abs(Opacity - _fadeTarget) > 0.001)
        {
            return;
        }

        Opacity = _fadeTarget;
        _fade.Stop();

        // Fully faded: hide the window outright so nothing is painted or composited behind apps.
        if (_fadeTarget == 0)
        {
            Visibility = Visibility.Hidden;
        }

        SyncAnimator();
    }

    /// <summary>Pins the overlay above apps, or back on the desktop layer, for the active mode.</summary>
    private void ApplyZOrder()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (_style.DisplayMode == DisplayModes.AlwaysOnTop)
        {
            Topmost = true;
        }
        else
        {
            Topmost = false;
            Win32.SendToDesktopLayer(handle);
        }
    }

    /// <summary>Hooks foreground/size events only while a feature actually needs them.</summary>
    private void SyncWatcher()
    {
        var needed = _style.DisplayMode == DisplayModes.DesktopOnly || _style.HideInFullscreen;
        if (!needed)
        {
            _foregroundWatch?.Dispose();
            _foregroundWatch = null;
            return;
        }

        _foregroundWatch ??= new ForegroundWatch(Dispatcher, EvaluateScreenState);
    }

    /// <summary>Recomputes suppression from Win32 hooks; never from a polling timer.</summary>
    private void EvaluateScreenState()
    {
        if (!IsLoaded)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;

        var fullscreen = _style.HideInFullscreen && DesktopGuard.IsFullscreenCovering(handle);
        if (fullscreen != _fullscreenSuppressed)
        {
            _fullscreenSuppressed = fullscreen;
            Log.Info(fullscreen
                ? "fullscreen app on this monitor; fading lyrics out"
                : "fullscreen app gone; restoring lyrics");
        }

        var covered = _style.DisplayMode == DisplayModes.DesktopOnly && !DesktopGuard.IsDesktopClear(handle);
        if (covered != _desktopSuppressed)
        {
            _desktopSuppressed = covered;
            Log.Info(covered
                ? "another app is active or covering the overlay; fading lyrics out"
                : "desktop clear; fading lyrics in");
        }

        ApplyZOrder();
        ApplyPresentation();
    }

    private void SetDisplayMode(string? mode)
    {
        var normalized = DisplayModes.Normalize(mode);
        _style.DisplayMode = normalized;
        _style.Save();
        Log.Info($"display mode -> {normalized}");
        SyncWatcher();
        EvaluateScreenState();
        _studio?.SyncState();
    }

    private void SetHideInFullscreen(bool enabled)
    {
        _style.HideInFullscreen = enabled;
        _style.Save();
        Log.Info(enabled ? "fullscreen auto-hide on" : "fullscreen auto-hide off");
        SyncWatcher();
        EvaluateScreenState();
        _studio?.SyncState();
    }

    /// <summary>Painting stops once the window is fully faded out or manually hidden.</summary>
    private bool RenderReady => !_hidden && Opacity > 0.01;

    /// <summary>True while a playing track is on screen, so the surface must keep animating.</summary>
    private bool Animating =>
        RenderReady && _playing && !_paused && _snapshot is { Title.Length: > 0 };

    /// <summary>Runs the 30 fps animator only while a playing line actually needs frames.</summary>
    private void SyncAnimator()
    {
        if (Animating == _animator.IsEnabled)
        {
            return;
        }

        if (Animating)
        {
            _animator.Start();
        }
        else
        {
            _animator.Stop();
        }
    }

    private async System.Threading.Tasks.Task PollAsync()
    {
        var snapshot = await MediaSessionService.ReadAsync();
        _snapshot = snapshot;

        if (snapshot is null)
        {
            _playing = false;
            _loadedTrackKey = null;
            _lyricsResult = null;
            ClearLyricState();
            ScreenAwake.Set(false);
            RefreshPaint();
            return;
        }

        _anchorMs = snapshot.Position.TotalMilliseconds;
        _anchorAt = DateTime.UtcNow;
        _playing = snapshot.IsPlaying;
        _durationMs = snapshot.Duration.TotalMilliseconds;
        ScreenAwake.Set(_style.KeepAwake && _playing && !_paused);

        if (_fetching || snapshot.Key == _loadedTrackKey)
        {
            RefreshPaint();
            return;
        }

        _loadedTrackKey = snapshot.Key;
        _lyricsResult = null;
        ClearLyricState();
        _offsetMs = 0; // the nudge is per track
        ApplyArtworkForTrack(snapshot.Key);
        RefreshPaint();

        _fetching = true;
        try
        {
            _lyricsResult = await _lyrics.GetAsync(snapshot);
        }
        finally
        {
            _fetching = false;
        }

        RefreshPaint();
    }

    /// <summary>Paints one authoritative frame after a poll and keeps the animator in step.</summary>
    private void RefreshPaint()
    {
        if (RenderReady)
        {
            Canvas.InvalidateVisual();
        }

        SyncAnimator();
    }

    private void OnAnimateFrame()
    {
        if (!Animating)
        {
            // Nothing plays or the overlay is hidden: stop the timer instead of ticking no-ops.
            SyncAnimator();
            return;
        }

        UpdateLineState();
        Canvas.InvalidateVisual();
    }

    private void UpdateLineState()
    {
        var document = _lyricsResult?.Document;
        if (document is null)
        {
            // No lyrics for this track: drop the old line, words and history instead of leaving
            // them on screen until the next synced track.
            if (_lineIndex != -1 || _lineText.Length > 0 || _words.Count > 0 ||
                _previousLine is not null || _history.Count > 0)
            {
                ClearLyricState();
            }

            return;
        }

        var index = LineIndexAt(document, PositionMs);
        if (index == _lineIndex)
        {
            return;
        }

        if (_lineIndex >= 0 && _lineIndex < document.Lines.Count)
        {
            _previousLine = document.Lines[_lineIndex].Text;
            if (!string.IsNullOrWhiteSpace(_previousLine))
            {
                _history.Add(_previousLine);
                if (_history.Count > 3)
                {
                    _history.RemoveAt(0);
                }
            }
        }

        _lineIndex = index;
        _lineChangedAt = DateTime.UtcNow;

        if (index >= 0 && index < document.Lines.Count)
        {
            var line = document.Lines[index];
            _lineText = line.Text;

            // The line's own window: its start to the next non-blank line, so a style's reveal
            // reaches 100% exactly when the next line begins.
            _lineStartMs = line.Time.TotalMilliseconds;
            _lineEndMs = line.Time.TotalMilliseconds + 6000;
            for (var i = index + 1; i < document.Lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(document.Lines[i].Text))
                {
                    _lineEndMs = document.Lines[i].Time.TotalMilliseconds;
                    break;
                }
            }

            var timings = _lyricsResult?.WordTimings;
            if (timings is not null && index < timings.Count && timings[index].Count > 0)
            {
                _words = timings[index];
            }
            else
            {
                var nextTime = index + 1 < document.Lines.Count
                    ? document.Lines[index + 1].Time
                    : line.Time + TimeSpan.FromSeconds(6);
                _words = WordTiming.Build(line, nextTime);
            }

            // The next two lines, so a style can show where the song is heading.
            _upcoming.Clear();
            for (var i = index + 1; i < document.Lines.Count && _upcoming.Count < 2; i++)
            {
                if (!string.IsNullOrWhiteSpace(document.Lines[i].Text))
                {
                    _upcoming.Add(document.Lines[i].Text);
                }
            }
        }
        else
        {
            _words = Array.Empty<TimedWord>();
            _lineText = string.Empty;
            _upcoming.Clear();
        }
    }

    /// <summary>Forgets the current lyric line and its word timings.</summary>
    private void ClearLyricState()
    {
        _lineIndex = -1;
        _lineText = string.Empty;
        _lineStartMs = 0;
        _lineEndMs = 0;
        _words = Array.Empty<TimedWord>();
        _previousLine = null;
        _history.Clear();
        _upcoming.Clear();
        _lineChangedAt = DateTime.UtcNow;
    }

    private static int LineIndexAt(LrcDocument document, double positionMs)
    {
        var found = -1;
        for (var i = 0; i < document.Lines.Count; i++)
        {
            if (document.Lines[i].Time.TotalMilliseconds > positionMs)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(document.Lines[i].Text))
            {
                found = i;
            }
        }

        return found;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        if (!RenderReady)
        {
            return;
        }

        // Nothing is drawn unless a track is actually playing: the overlay stays fully
        // transparent while idle instead of showing placeholder text or shortcut hints.
        var snapshot = _snapshot;
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Title) || !_playing || _paused)
        {
            if (_lineIndex != -1 || _lineText.Length > 0 || _words.Count > 0 ||
                _previousLine is not null || _history.Count > 0)
            {
                ClearLyricState();
            }

            return;
        }

        _regular ??= SKTypeface.FromFamilyName(_style.Font, SKFontStyle.Normal) ?? SKTypeface.Default;
        _bold ??= SKTypeface.FromFamilyName(_style.Font, SKFontStyle.Bold) ?? _regular;
        _mono ??= TextFonts.Mono(_regular);

        float width = info.Width;
        float height = info.Height;

        // Full screen pins the window to the display and centres a zoomed lyric band on it.
        var fullScreen = _style.FullScreen;
        var stageHeight = Math.Min(OverlayLayout.StageHeight, height);

        // One fallback decision per frame for every string this frame draws: a Japanese, Chinese
        // or Korean title needs a face with those glyphs just as much as the lyric line does.
        var sample = string.Join(" ", new[] { _snapshot?.Title, _snapshot?.Artist, _lineText, _previousLine }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var regular = TextFonts.ForText(_regular, sample);
        var bold = TextFonts.ForText(_bold, sample);
        var mono = TextFonts.ForText(_mono, sample);

        var alpha = _paused ? 0.45f : _playing ? 1f : 0.7f;

        var result = _lyricsResult;
        if (result is null)
        {
            // Still looking the lyrics up: draw nothing at all rather than a bare progress bar.
            return;
        }

        if (result.State != LyricsState.Synced || result.Document is null)
        {
            // Missing, instrumental or failed: render nothing at all rather than a fallback line.
            ClearLyricState();
            return;
        }

        // The title belongs to full-screen mode only, pinned to the top-left of the display.
        if (fullScreen)
        {
            KaraokeRenderer.DrawHeader(canvas, _style, bold, snapshot.Title, snapshot.Artist,
                width, height, alpha, leftAligned: true);
        }

        if (_styleKind == StyleKind.KineticScatter)
        {
            // Full-screen exclusive: the surface is the viewport, so lay out in surface pixels
            // with no extra transform, and every word can reach the real edges of the display.
            ScatterRenderer.Draw(canvas, _style, bold, _words, PositionMs, _lineIndex, _history,
                width, height, alpha, _playing && !_paused);

            KaraokeRenderer.DrawProgress(canvas, _style, width, height, PositionMs, _durationMs, alpha);
            return;
        }

        // From here the styles draw inside the centred lyric band; the progress bar stays at the bottom.
        canvas.Save();
        if (fullScreen)
        {
            // Zoom around the screen centre so every style renders larger, while long lines still
            // keep their fit inside the full screen width.
            var logicalWidth = width / OverlayLayout.FullScreenZoom;
            canvas.Translate(width / 2f, height / 2f);
            canvas.Scale(OverlayLayout.FullScreenZoom, OverlayLayout.FullScreenZoom);
            canvas.Translate(-logicalWidth / 2f, -stageHeight / 2f);
            width = logicalWidth;
        }

        height = stageHeight;

        var positionMs = PositionMs;
        var enter = (float)Math.Clamp((DateTime.UtcNow - _lineChangedAt).TotalMilliseconds / 220.0, 0, 1);

        if (enter < 1f && !string.IsNullOrWhiteSpace(_previousLine) &&
            _styleKind is not (StyleKind.Bubble or StyleKind.NativeDesign))
        {
            var outgoing = KaraokeRenderer.EaseOutCubic(enter);
            KaraokeRenderer.DrawSecondaryLine(canvas, _style, regular, _previousLine,
                width, height * 0.30f + outgoing * 10f, alpha * (1 - outgoing) * 0.5f, 0.9f);
        }

        var context = new StyleContext(
            canvas,
            _style,
            regular,
            bold,
            mono,
            _words,
            positionMs,
            _lineStartMs,
            _lineEndMs,
            width,
            height,
            enter,
            alpha,
            _playing && !_paused,
            _history,
            _upcoming,
            _lineText,
            WordTiming.ContainsCjk(_lineText),
            CurrentLineIndex(result.Document, positionMs));

        StyleRenderers.Draw(_styleKind, context);

        canvas.Restore();

        // Only the active line is drawn; the upcoming line is deliberately not previewed.
        KaraokeRenderer.DrawProgress(canvas, _style, info.Width, info.Height, positionMs, _durationMs, alpha);
    }
}
