using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LyricOverlay.Diagnostics;
using LyricOverlay.Services;

namespace LyricOverlay.Lyrics;

public enum LyricsState
{
    Synced,
    Instrumental,
    NotFound,
    Error,
}

/// <summary>Where timed lyrics come from. The first is tried first; the other is the fallback.</summary>
public enum LyricsSource
{
    LrcRed,
    Lrclib,
}

public static class LyricsSources
{
    public static string NameOf(LyricsSource source) => source switch
    {
        LyricsSource.LrcRed => "lrc.red",
        _ => "LRCLIB",
    };

    public static LyricsSource Parse(string? value) =>
        string.Equals(value, "lrclib", StringComparison.OrdinalIgnoreCase) ? LyricsSource.Lrclib : LyricsSource.LrcRed;

    public static string Credit(LyricsSource source) => source == LyricsSource.LrcRed ? "Lyrics by lrc.red" : "Lyrics by LRCLIB";
}

public sealed record LyricsResult(
    LyricsState State,
    LrcDocument? Document,
    string? Message,
    IReadOnlyList<IReadOnlyList<TimedWord>>? WordTimings = null);

/// <summary>
/// Resolves synced lyrics for a track. A local <c>lyrics/&lt;Artist&gt; - &lt;Title&gt;.lrc</c>
/// file always wins, so you can hand-time a track and keep it. Otherwise both providers are
/// queried at once and the selected one (lrc.red by default) gets first refusal on the result.
/// </summary>
public sealed class LyricsProvider
{
    private readonly LrcRedProvider _lrcRed = new();
    private readonly LrclibProvider _lrclib = new();
    private readonly Dictionary<(string Key, LyricsSource Source), LyricsResult> _cache = new();

    /// <summary>Provider tried first. Changing it clears the cache.</summary>
    public LyricsSource Source { get; private set; } = LyricsSource.LrcRed;

    public void SetSource(LyricsSource source)
    {
        if (Source == source)
        {
            return;
        }

        Source = source;
        _cache.Clear();
        Log.Info($"lyrics source -> {LyricsSources.NameOf(source)}");
    }

    public async Task<LyricsResult> GetAsync(PlaybackSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Title))
        {
            return new LyricsResult(LyricsState.NotFound, null, "no track");
        }

        if (_cache.TryGetValue((snapshot.Key, Source), out var cached))
        {
            return cached;
        }

        var result = await ResolveAsync(snapshot);
        _cache[(snapshot.Key, Source)] = result;
        Log.Info($"lyrics for \"{snapshot.Title}\" by \"{snapshot.Artist}\": {result.State} ({result.Message})");
        return result;
    }

    private async Task<LyricsResult> ResolveAsync(PlaybackSnapshot snapshot)
    {
        var local = TryLoadLocal(snapshot);
        if (local is not null)
        {
            return new LyricsResult(LyricsState.Synced, local, "local .lrc");
        }

        var alternate = Source == LyricsSource.LrcRed ? LyricsSource.Lrclib : LyricsSource.LrcRed;

        // Both lookups start together, so a provider that has no match no longer keeps the other
        // from starting until its own request times out. The prior provider's result is kept and
        // only used if the preferred one comes back empty.
        var preferredTask = QueryAsync(Source, snapshot);
        var alternateTask = QueryAsync(alternate, snapshot);

        var preferred = await preferredTask;
        if (preferred.State == LyricsState.Synced)
        {
            Forget(alternateTask);
            Log.Info($"lyrics from {LyricsSources.NameOf(Source)} (preferred): {preferred.Message}");
            return preferred;
        }

        var fallback = await alternateTask;
        if (fallback.State == LyricsState.Synced)
        {
            Log.Info($"lyrics from {LyricsSources.NameOf(alternate)} (fallback): {LyricsSources.NameOf(Source)} missed ({preferred.Message})");
            return fallback;
        }

        // Report the more specific of the two failures.
        Log.Info($"both providers missed ({LyricsSources.NameOf(Source)}: {preferred.Message}; {LyricsSources.NameOf(alternate)}: {fallback.Message})");
        return preferred.State == LyricsState.Error ? preferred : fallback.State == LyricsState.Error ? fallback : preferred;
    }

    /// <summary>
    /// Lets a leftover request finish without waiting on it. The providers turn their own failures
    /// into results, but observing the task keeps a late fault out of the unobserved-exception path.
    /// </summary>
    private static void Forget(Task task) =>
        task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

    private Task<LyricsResult> QueryAsync(LyricsSource source, PlaybackSnapshot snapshot) => source switch
    {
        LyricsSource.LrcRed => _lrcRed.GetAsync(snapshot),
        _ => _lrclib.GetAsync(snapshot),
    };

    private static LrcDocument? TryLoadLocal(PlaybackSnapshot snapshot)
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var folder = Path.Combine(root, "lyrics");
            if (!Directory.Exists(folder))
            {
                continue;
            }

            var candidates = new[]
            {
                SafeFileName($"{snapshot.Artist} - {snapshot.Title}") + ".lrc",
                SafeFileName(snapshot.Title) + ".lrc",
            };

            foreach (var candidate in candidates)
            {
                var path = Path.Combine(folder, candidate);
                if (File.Exists(path))
                {
                    var parsed = LrcDocument.Parse(File.ReadAllText(path));
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }
            }
        }

        return null;
    }

    private static string SafeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Trim();
    }
}
