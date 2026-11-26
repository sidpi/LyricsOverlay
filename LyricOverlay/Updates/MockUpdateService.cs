using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LyricOverlay.Updates;

/// <summary>
/// Preview backend: same sequence and timings as the real updater, but the release is
/// hard-coded, the "download" is simulated and nothing is installed. Enabled with
/// <c>--update-preview</c> or <c>LYRICOVERLAY_UPDATE_MOCK=1</c>.
/// </summary>
internal sealed class MockUpdateService : IUpdateService
{
    public const long InstallerSize = 27_918_336; // ≈ 26.6 MB

    private const string Notes =
        "What's new in 1.6.0\n\n" +
        "• Word-by-word timing for Hindi and Devanagari tracks\n" +
        "• New Scatter style with per-word drift\n" +
        "• The studio remembers its last window size\n" +
        "• Faster lyric matching when a track restarts\n" +
        "• Fixed the overlay blinking when switching output devices\n\n" +
        "Thanks for the reports — keep them coming.";

    public static UpdateInfo Sample { get; } = new(
        "1.6.0",
        "1.0.0",
        Notes,
        DateTimeOffset.Now.AddDays(-2),
        new Uri("https://example.invalid/LyricsOverlay-Setup-1.6.0.exe"),
        "LyricsOverlay-Setup-1.6.0.exe",
        InstallerSize,
        null);

    public string CurrentVersion => "1.0.0";

    public bool Simulated => true;

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken);
        return Sample;
    }

    public async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        const int steps = 46;
        var total = InstallerSize;
        var step = total / (double)steps;
        var random = new Random(7);
        var watch = Stopwatch.StartNew();
        long downloaded = 0;

        progress.Report(new DownloadProgress(0, total, 0));
        while (downloaded < total)
        {
            await Task.Delay(110 + random.Next(0, 70), cancellationToken);

            downloaded = Math.Min(total, (long)Math.Round(downloaded + step * (0.85 + random.NextDouble() * 0.4)));
            var seconds = Math.Max(0.001, watch.Elapsed.TotalSeconds);
            progress.Report(new DownloadProgress(downloaded, total, downloaded / seconds));
        }

        return Path.Combine(Path.GetTempPath(), info.InstallerName);
    }

    public Task VerifyAsync(string path, UpdateInfo info, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(320), cancellationToken);

    public Task InstallAsync(string path, UpdateInfo info, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal static class UpdateServices
{
    /// <summary>Mock mode can be forced without a GitHub release: LYRICOVERLAY_UPDATE_MOCK=1.</summary>
    public static IUpdateService Create() =>
        string.Equals(Environment.GetEnvironmentVariable("LYRICOVERLAY_UPDATE_MOCK"), "1",
            StringComparison.OrdinalIgnoreCase)
            ? new MockUpdateService()
            : new UpdateService();
}
