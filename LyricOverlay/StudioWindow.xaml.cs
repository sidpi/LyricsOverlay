using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LyricOverlay.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace LyricOverlay;

/// <summary>
/// The studio UI, hosted in the same process as the overlay. It talks to the overlay
/// through WebView2 messages, so there is no pipe, no polling races and no second app.
/// </summary>
public partial class StudioWindow : Window
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private readonly OverlayWindow _overlay;
    private readonly DispatcherTimer _push = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _ready;
    private bool _allowClose;

    public StudioWindow(OverlayWindow overlay)
    {
        _overlay = overlay;
        InitializeComponent();

        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 5, 5, 7);
        Loaded += async (_, _) => await InitAsync();
        _push.Tick += (_, _) => PushState();

        Closing += (_, e) =>
        {
            // Closing the studio parks the app in the tray; only an app shutdown really closes it.
            if (_allowClose || Application.Current.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        };
    }

    /// <summary>Lets the window close for real when the app is quitting.</summary>
    public void AllowClose() => _allowClose = true;

    public void Present()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
    }

    private async Task InitAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
            var core = Web.CoreWebView2;
            var devUrl = Environment.GetEnvironmentVariable("LYRICOVERLAY_STUDIO_DEV");
            var development = !string.IsNullOrWhiteSpace(devUrl);

            // Shipped builds never expose DevTools or browser accelerators.
            core.Settings.AreDevToolsEnabled = development;
            core.Settings.AreBrowserAcceleratorKeysEnabled = development;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += (_, e) => Log.Error($"studio webview failed: {e.ProcessFailedKind}", null);

            var studioDir = Path.Combine(AppContext.BaseDirectory, "Studio");

            if (development)
            {
                core.Navigate(devUrl);
            }
            else if (File.Exists(Path.Combine(studioDir, "index.html")))
            {
                core.SetVirtualHostNameToFolderMapping(
                    "studio.lyricoverlay",
                    studioDir,
                    CoreWebView2HostResourceAccessKind.Allow);
                core.Navigate("https://studio.lyricoverlay/index.html");
            }
            else
            {
                Fallback.Visibility = Visibility.Visible;
                Log.Info("studio assets missing; run npm run build in LyricsOverlay");
                return;
            }

            _ready = true;
            _push.Start();
            Log.Info($"studio webview ready (assets={studioDir})");
        }
        catch (Exception ex)
        {
            Log.Error("studio webview failed to start", ex);
            Fallback.Visibility = Visibility.Visible;
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement request;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            request = document.RootElement.Clone();
        }
        catch
        {
            return;
        }

        var id = request.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var value)
            ? value
            : 0;
        var command = request.TryGetProperty("cmd", out var cmdElement) ? cmdElement.GetString() ?? "" : "";

        if (string.Equals(command, "window", StringComparison.OrdinalIgnoreCase))
        {
            HandleWindowAction(request.TryGetProperty("action", out var action) ? action.GetString() : null);
            Reply(id, ok: true, state: null);
            return;
        }

        try
        {
            // Reuse the overlay's control surface verbatim: {"ok":true,"state":{...}}.
            var response = _overlay.ControlRequest(request).Trim();
            // Splice the request id into the overlay's own reply: {"id":1,"ok":true,"state":{…}}.
            Post(response.StartsWith('{')
                ? $"{{\"id\":{id},{response[1..]}"
                : $"{{\"id\":{id},\"ok\":false}}");
        }
        catch (Exception ex)
        {
            Log.Error("studio request failed", ex);
            Reply(id, ok: false, state: null);
        }
    }

    private void HandleWindowAction(string? action)
    {
        switch (action)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "maximize":
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case "close":
                Hide();
                break;
            case "drag":
                StartDrag();
                break;
        }
    }

    private void StartDrag()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    private void PushState()
    {
        if (!_ready)
        {
            return;
        }

        try
        {
            Post(JsonSerializer.Serialize(new { type = "state", state = _overlay.StateSnapshot() }));
        }
        catch (Exception ex)
        {
            Log.Error("studio state push failed", ex);
        }
    }

    private void Reply(long id, bool ok, object? state) =>
        Post(JsonSerializer.Serialize(new { id, ok, state }));

    private void Post(string json)
    {
        Web.CoreWebView2?.PostWebMessageAsJson(json);
    }
}
