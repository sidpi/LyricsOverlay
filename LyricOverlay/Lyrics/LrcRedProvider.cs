using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LyricOverlay.Diagnostics;
using LyricOverlay.Services;

namespace LyricOverlay.Lyrics;

/// <summary>
/// lrc.red: word/line-timed lyrics for ~30M tracks.
///
/// Their terms require an identifying User-Agent (app name + link + contact) and a visible
/// credit wherever the data is shown. Set <see cref="Contact"/> to a real address before
/// shipping this to anyone else — bulk anonymous traffic gets the IP blocked.
/// </summary>
public sealed class LrcRedProvider
{
    private const string Base = "https://lrc.red";

    /// <summary>App name, link and contact, per the lrc.red API terms.</summary>
    private const string Contact = "https://github.com/lyricoverlay/lyricoverlay; lyricoverlay@users.noreply.github.com";

    private const string UserAgent = "LyricOverlay/1.0 (+" + Contact + ")";

    /// <summary>lrc.red reports whole-track durations, so a few seconds of slack is plenty.</summary>
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(6);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public LrcRedProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<LyricsResult> GetAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Title))
        {
            return new LyricsResult(LyricsState.NotFound, null, "no track");
        }

        var hit = await FindAsync(snapshot, cancellationToken);
        if (hit is null)
        {
            return new LyricsResult(LyricsState.NotFound, null, "not on lrc.red");
        }

        var isrc = hit.Value.Isrc;
        var meta = await GetTrackAsync(isrc, cancellationToken);
        var sync = meta?.Sync ?? "none";

        if (sync.Equals("word", StringComparison.OrdinalIgnoreCase) && meta?.LyricsFile is { } lyricsFileUrl)
        {
            var yaml = await GetStringAsync(lyricsFileUrl, cancellationToken);
            var wordTimed = LyricsFileParser.Parse(yaml);
            if (wordTimed is not null)
            {
                return new LyricsResult(LyricsState.Synced, wordTimed.Value.Document, "lrc.red word sync", wordTimed.Value.Words);
            }
        }

        if (meta?.Lrc is { } lrcUrl)
        {
            var lrc = await GetStringAsync(lrcUrl, cancellationToken);
            var parsed = LrcDocument.Parse(lrc);
            if (parsed is not null)
            {
                return new LyricsResult(LyricsState.Synced, parsed, "lrc.red");
            }
        }

        if (meta is { Lines: <= 0 } && sync.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return new LyricsResult(LyricsState.Instrumental, null, "lrc.red: no timed lyrics");
        }

        return new LyricsResult(LyricsState.NotFound, null, "lrc.red: no timed lyrics");
    }

    /// <summary>Best search hit for this track: exact title, matching artist, duration within tolerance.</summary>
    private async Task<Hit?> FindAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken)
    {
        var query = $"{snapshot.Title} {snapshot.Artist}".Trim();
        var url = $"{Base}/search.json?q={Uri.EscapeDataString(query)}";
        var body = await GetStringAsync(url, cancellationToken);
        if (body is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var title = Normalize(snapshot.Title);
            var artist = Normalize(snapshot.Artist);
            var playerDuration = snapshot.Duration.TotalSeconds;

            var scored = new List<(double Score, Hit Hit)>();
            foreach (var hit in hits.EnumerateArray())
            {
                var candidate = new Hit(
                    hit.TryGetProperty("isrc", out var isrc) ? isrc.GetString() ?? string.Empty : string.Empty,
                    hit.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                    hit.TryGetProperty("artist", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                    hit.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0);

                if (candidate.Isrc.Length == 0 || Normalize(candidate.Title) != title)
                {
                    continue;
                }

                if (!ArtistMatches(artist, Normalize(candidate.Artist)))
                {
                    continue;
                }

                var delta = Math.Abs(candidate.Duration - playerDuration);
                if (playerDuration > 1 && candidate.Duration > 1 && delta > DurationTolerance.TotalSeconds)
                {
                    continue;
                }

                // Closest duration wins; a missing duration scores just behind an exact one.
                scored.Add((delta, candidate));
            }

            if (scored.Count == 0)
            {
                return null;
            }

            var best = scored.OrderBy(s => s.Score).First().Hit;
            Log.Info($"lrc.red match: \"{best.Title}\" by \"{best.Artist}\" ({best.Duration:0}s) for {playerDuration:0}s");
            return best;
        }
        catch (Exception ex)
        {
            Log.Error("lrc.red search parse failed", ex);
            return null;
        }
    }

    private async Task<TrackMeta?> GetTrackAsync(string isrc, CancellationToken cancellationToken)
    {
        var body = await GetStringAsync($"{Base}/s/{Uri.EscapeDataString(isrc)}.json", cancellationToken);
        if (body is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? lrc = null;
            string? lyricsFile = null;
            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object)
            {
                if (files.TryGetProperty("lrc", out var lrcUrl) && lrcUrl.ValueKind == JsonValueKind.String)
                {
                    lrc = Absolute(lrcUrl.GetString());
                }

                if (files.TryGetProperty("lyricsfile", out var sfUrl) && sfUrl.ValueKind == JsonValueKind.String)
                {
                    lyricsFile = Absolute(sfUrl.GetString());
                }
            }

            return new TrackMeta(
                root.TryGetProperty("sync", out var sync) ? sync.GetString() ?? "none" : "none",
                root.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Number ? lines.GetInt32() : 0,
                lrc,
                lyricsFile);
        }
        catch (Exception ex)
        {
            Log.Error("lrc.red track parse failed", ex);
            return null;
        }
    }

    private async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"lrc.red HTTP {(int)response.StatusCode} for {url}");
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error($"lrc.red request failed: {url}", ex);
            return null;
        }
    }

    private static string Absolute(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{Base}/{url.TrimStart('/')}";
    }

    /// <summary>Lowercase, strip bracketed extras, punctuation and version words.</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text.ToLowerInvariant();
        value = Regex.Replace(value, @"[\(\[\{][^\)\]\}]*[\)\]\}]", " ");
        value = Regex.Replace(value, @"\b(feat|ft|featuring|with|remaster(ed)?|version|edit|single)\b.*$", " ");
        value = new string(value.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static bool ArtistMatches(string wanted, string candidate)
    {
        if (wanted.Length == 0 || candidate.Length == 0)
        {
            return false;
        }

        if (wanted == candidate)
        {
            return true;
        }

        // Collaboration credits: every token of the wanted artist must appear in the candidate.
        var wantedTokens = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return wantedTokens.Length > 0 && wantedTokens.All(candidateTokens.Contains);
    }

    private readonly record struct Hit(string Isrc, string Title, string Artist, double Duration);

    private sealed record TrackMeta(string Sync, int Lines, string? Lrc, string? LyricsFile);
}
