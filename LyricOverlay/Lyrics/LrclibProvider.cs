using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LyricOverlay.Diagnostics;
using LyricOverlay.Services;

namespace LyricOverlay.Lyrics;

/// <summary>
/// LRCLIB (lrclib.net): free, keyless, community lyrics. The exact endpoint is tried first;
/// the search fallback must filter by duration, because the same title and artist are
/// published as radio edits, live versions and 30-second snippets alike.
/// </summary>
public sealed class LrclibProvider
{
    /// <summary>
    /// How far a search hit's duration may differ from the playing track. Anything looser
    /// starts matching other edits, which is the classic cause of drifting lyrics.
    /// </summary>
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2.5);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public LrclibProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LyricOverlay/1.0 (local desktop overlay)");
    }

    public async Task<LyricsResult> GetAsync(PlaybackSnapshot snapshot)
    {
        try
        {
            var seconds = snapshot.Duration.TotalSeconds;
            var duration = seconds is > 1 and < 3600 ? $"&duration={(int)Math.Round(seconds)}" : string.Empty;

            var url = "https://lrclib.net/api/get" +
                      $"?artist_name={Uri.EscapeDataString(snapshot.Artist)}" +
                      $"&track_name={Uri.EscapeDataString(snapshot.Title)}" +
                      $"&album_name={Uri.EscapeDataString(snapshot.Album)}" +
                      duration;

            using var response = await _http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return ReadRecord(await response.Content.ReadAsStringAsync());
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return await SearchAsync(snapshot);
            }

            return new LyricsResult(LyricsState.Error, null, $"lrclib HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Log.Error("lrclib lookup failed", ex);
            return new LyricsResult(LyricsState.Error, null, ex.Message);
        }
    }

    /// <summary>
    /// Filters search hits by duration and prefers word-level timings, instead of taking
    /// whichever synced record happens to come back first.
    /// </summary>
    private async Task<LyricsResult> SearchAsync(PlaybackSnapshot snapshot)
    {
        var url = "https://lrclib.net/api/search" +
                  $"?track_name={Uri.EscapeDataString(snapshot.Title)}" +
                  $"&artist_name={Uri.EscapeDataString(snapshot.Artist)}";

        using var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return new LyricsResult(LyricsState.NotFound, null, "not on lrclib");
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new LyricsResult(LyricsState.NotFound, null, "not on lrclib");
        }

        var playerSeconds = snapshot.Duration.TotalSeconds;
        var candidates = new List<(double Delta, bool WordSync, LyricsResult Result)>();

        foreach (var record in json.RootElement.EnumerateArray())
        {
            var recordSeconds = record.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number
                ? duration.GetDouble()
                : 0;

            if (playerSeconds > 1 && recordSeconds > 1 &&
                Math.Abs(recordSeconds - playerSeconds) > DurationTolerance.TotalSeconds)
            {
                continue; // a different edit, live take or snippet
            }

            var result = ReadRecord(record.GetRawText());
            if (result.State != LyricsState.Synced)
            {
                continue;
            }

            var wordSync = record.TryGetProperty("lyricsfile", out var file) && file.ValueKind == JsonValueKind.String;
            var delta = recordSeconds > 1 ? Math.Abs(recordSeconds - playerSeconds) : double.MaxValue;
            candidates.Add((delta, wordSync, result));
        }

        if (candidates.Count == 0)
        {
            return new LyricsResult(LyricsState.NotFound, null, "no lrclib match at this duration");
        }

        var best = candidates
            .OrderBy(c => c.Delta)
            .ThenByDescending(c => c.WordSync)
            .First();

        Log.Info($"lrclib search match: {best.Result.Message} (delta {best.Delta:0.0}s of {candidates.Count} candidates)");
        return best.Result;
    }

    private static LyricsResult ReadRecord(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("instrumental", out var instrumental) &&
            instrumental.ValueKind == JsonValueKind.True)
        {
            return new LyricsResult(LyricsState.Instrumental, null, "instrumental");
        }

        // Word-level timings, when the record ships them, beat interpolated line-level ones.
        if (root.TryGetProperty("lyricsfile", out var lyricsFile) &&
            lyricsFile.ValueKind == JsonValueKind.String)
        {
            var wordTimed = LyricsFileParser.Parse(lyricsFile.GetString());
            if (wordTimed is not null)
            {
                return new LyricsResult(LyricsState.Synced, wordTimed.Value.Document, "lrclib word sync", wordTimed.Value.Words);
            }
        }

        if (root.TryGetProperty("syncedLyrics", out var synced) &&
            synced.ValueKind == JsonValueKind.String)
        {
            var parsed = LrcDocument.Parse(synced.GetString());
            if (parsed is not null)
            {
                return new LyricsResult(LyricsState.Synced, parsed, "lrclib.net");
            }
        }

        return new LyricsResult(LyricsState.NotFound, null, "no synced lyrics");
    }
}
