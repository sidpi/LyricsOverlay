using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LyricOverlay.Diagnostics;
using LyricOverlay.Lyrics;
using LyricOverlay.Services;

namespace LyricOverlay;

public partial class App : Application
{
    /// <summary>Reverse-domain identity shared by the shortcut, taskbar and installer.</summary>
    public const string AppUserModelId = "com.lyricsoverlay.app";

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // Cosmetic only: grouping falls back to the executable path.
        }

        var renderIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--render", StringComparison.OrdinalIgnoreCase));
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            try
            {
                // Run off the dispatcher: awaiting on the WPF synchronization context here would deadlock.
                System.Threading.Tasks.Task.Run(() => Rendering.HeadlessRenderer.RenderAsync(e.Args, renderIndex + 1))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"render failed: {ex}");
            }

            Shutdown();
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--bench", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Rendering.HeadlessRenderer.Benchmark(e.Args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"bench failed: {ex}");
            }

            Shutdown();
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            // Run off the dispatcher: awaiting on the WPF synchronization context here would deadlock.
            System.Threading.Tasks.Task.Run(RunSelfTestAsync).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        // Local preview of every update state, with no GitHub call and nothing installed.
        if (e.Args.Any(a => string.Equals(a, "--update-preview", StringComparison.OrdinalIgnoreCase)))
        {
            var preview = new Updates.UpdatePreviewWindow();
            MainWindow = preview;
            preview.Show();
            return;
        }

        // One app at a time: a second launch just brings the running studio forward.
        _instance = new Mutex(initiallyOwned: true, @"Local\LyricOverlay.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        var window = new OverlayWindow();
        MainWindow = window;
        window.Show();

        // Launched by Windows at sign-in: stay in the tray until the studio is opened.
        if (!e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)))
        {
            window.OpenStudio();
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ControlServer.PipeName, PipeDirection.InOut);
            pipe.Connect(900);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(pipe);
            writer.WriteLine("{\"cmd\":\"studio\"}");
            reader.ReadLine();
        }
        catch
        {
            // The other instance is still starting; its tray icon is already there.
        }
    }

    /// <summary>
    /// Headless check: reads the current system media session and resolves lyrics for it,
    /// printing the result. Used to verify the pipeline without a visible window.
    /// </summary>
    private static async Task RunSelfTestAsync()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        Console.WriteLine("== LyricOverlay self-test ==");
        try
        {
            var snapshot = await MediaSessionService.ReadAsync();
            if (snapshot is null)
            {
                Console.WriteLine("session: none (no app is reporting a media session)");
                return;
            }

            Console.WriteLine($"session: {snapshot.Title} — {snapshot.Artist}");
            Console.WriteLine($"album:   {snapshot.Album}");
            Console.WriteLine($"state:   {(snapshot.IsPlaying ? "playing" : "paused")}, " +
                              $"position {snapshot.Position:hh\\:mm\\:ss\\.ff} / {snapshot.Duration:hh\\:mm\\:ss}");
            Console.WriteLine($"source:  {snapshot.SourceApp}");

            var provider = new LyricsProvider();
            provider.SetSource(LyricsSources.Parse(Rendering.OverlayStyle.Load().LyricsProvider));
            Console.WriteLine($"provider:{new string(' ', 1)}{LyricsSources.NameOf(provider.Source)}");

            var result = await provider.GetAsync(snapshot);
            Console.WriteLine($"lyrics:  {result.State} ({result.Message}), lines={result.Document?.Lines.Count ?? 0}");

            var artwork = await AlbumArtColors.GetAsync(snapshot.Key);
            Console.WriteLine(artwork is null
                ? "artwork: no dominant colour"
                : $"artwork: highlight {artwork.Value.Highlight}, glow {artwork.Value.Glow}");

            if (result.Document is { } doc && doc.Lines.Count > 0)
            {
                Console.WriteLine($"current: {doc.LineAt(snapshot.Position)?.Text ?? "(no line yet)"}");
                Console.WriteLine($"next:    {doc.NextAfter(snapshot.Position)?.Text ?? "(end)"}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("self-test failed", ex);
            Console.WriteLine($"FAILED: {ex.Message}");
            throw;
        }
    }
}
