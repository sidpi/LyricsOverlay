using System;
using System.IO;
using System.Threading.Tasks;
using LyricOverlay.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace LyricOverlay.Services;

/// <summary>
/// One Evergreen <see cref="CoreWebView2Environment"/> shared by every WebView2 the app hosts
/// (the studio, and any future surface): same runtime, same user data folder, same lightweight
/// browser arguments. The Edge runtime itself is never bundled; it comes from Windows.
/// </summary>
internal static class WebViewEnvironment
{
    // Small single-window browser: no extensions, no background traffic, one renderer at most.
    private const string BrowserArguments =
        "--disable-extensions --disable-background-networking --disable-default-apps --renderer-process-limit=1";

    private static readonly object Gate = new();
    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>The shared environment, created once per process and retried if creation failed.</summary>
    public static Task<CoreWebView2Environment> GetAsync()
    {
        lock (Gate)
        {
            if (_environment is null || _environment.IsFaulted || _environment.IsCanceled)
            {
                _environment = CreateAsync();
            }

            return _environment;
        }
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        // A stable, app-owned profile folder: caches and permissions survive restarts, and the
        // studio always sees the same storage instead of a per-exe-name default.
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricOverlay",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);

        try
        {
            var options = new CoreWebView2EnvironmentOptions(BrowserArguments);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            Log.Info($"webview2 environment ready (evergreen, data={userDataFolder})");
            return environment;
        }
        catch (Exception ex)
        {
            Log.Error("webview2 environment failed to start", ex);
            throw;
        }
    }
}