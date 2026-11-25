using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using LyricOverlay.Diagnostics;
using LyricOverlay.Lyrics;
using LyricOverlay.Services;

namespace LyricOverlay.Rendering;

/// <summary>
/// Renders a single settled frame to a PNG with the same style renderers the overlay uses,
/// so glow coverage and clipping can be checked without a live window.
/// </summary>
internal static class HeadlessRenderer
{
    private const string SampleLine = "Glow covers every glyph — gypsy jumps quickly";

    /// <summary>
    /// <c>--render out.png [--style Name] [--width 1000] [--height 240] [--text "…"]
    /// [--history "older line|oldest line"] [--position 0.45] [--bg #0E0E12|transparent]
    /// [--fullscreen]</c>
    /// </summary>
    public static async Task<int> RenderAsync(string[] args, int pathIndex)
    {
        var output = args[pathIndex];
        var style = OverlayStyle.Load();

        if (GetArg(args, "--style") is { Length: > 0 } styleName)
        {
            style.Style = styleName;
        }

        var kind = StyleCatalog.Parse(style.Style);
        var width = GetIntArg(args, "--width", 1000);
        var height = GetIntArg(args, "--height", 240);
        var fraction = GetDoubleArg(args, "--position", 0.45);
        var explicitText = GetArg(args, "--text");
        var explicitHistory = GetArg(args, "--history")?.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var background = ParseBackground(GetArg(args, "--bg"));
        var fullScreen = args.Any(a => string.Equals(a, "--fullscreen", StringComparison.OrdinalIgnoreCase));

        // Explicit text means a deterministic frame: no session lookup, no network.
        var snapshot = explicitText is null ? await MediaSessionService.ReadAsync() : null;
        LyricsResult? lyrics = null;
        if (snapshot is not null)
        {
            lyrics = await new LyricsProvider().GetAsync(snapshot);
        }

        var frame = BuildFrame(snapshot, lyrics, explicitText, explicitHistory, fraction);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(background);

        var regular = SKTypeface.FromFamilyName(style.Font, SKFontStyle.Normal) ?? SKTypeface.Default;
        var bold = SKTypeface.FromFamilyName(style.Font, SKFontStyle.Bold) ?? regular;
        var mono = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal) ?? regular;

        // Enter/alpha at their settled values: this is the frame the overlay draws mid-line.
        Draw(canvas, info, style, kind, regular, bold, mono, snapshot, lyrics, frame,
            positionMs: frame.PositionMs,
            durationMs: snapshot?.Duration.TotalMilliseconds ?? 210000,
            enter: 1f,
            alpha: 1f,
            playing: true,
            fullScreen: fullScreen);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(output, data.ToArray());

        Console.WriteLine($"rendered {kind} frame → {output}");
        Console.WriteLine($"  size:   {width}x{height}");
        Console.WriteLine($"  line:   {frame.LineText}");
        Console.WriteLine($"  tokens: {frame.Words.Count} ({string.Join("|", frame.Words.Take(8).Select(w => w.Text))}…)");
        Console.WriteLine($"  timing: {(lyrics?.WordTimings is not null ? "word-synced" : "interpolated")}");
        Console.WriteLine($"  font:   {style.Font} @ {style.TextScale:P0}, glow tint {style.GlowTint}");
        return 0;
    }

    private sealed record Frame(
        IReadOnlyList<TimedWord> Words,
        string LineText,
        int LineIndex,
        IReadOnlyList<string> History,
        double PositionMs);

    private static Frame BuildFrame(PlaybackSnapshot? snapshot, LyricsResult? lyrics, string? explicitText,
        string[]? explicitHistory, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);

        if (!string.IsNullOrWhiteSpace(explicitText))
        {
            return Synthetic(explicitText, explicitHistory, fraction);
        }

        if (lyrics?.Document is { Lines.Count: > 0 } document)
        {
            // Pick the longest line with word data: best case for judging glow coverage.
            var index = 0;
            var best = -1;
            for (var i = 0; i < document.Lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(document.Lines[i].Text) && document.Lines[i].Text.Length > best)
                {
                    best = document.Lines[i].Text.Length;
                    index = i;
                }
            }

            var line = document.Lines[index];
            var nextTime = index + 1 < document.Lines.Count
                ? document.Lines[index + 1].Time
                : line.Time + TimeSpan.FromSeconds(6);

            var words = lyrics.WordTimings is { } timings && index < timings.Count && timings[index].Count > 0
                ? timings[index]
                : WordTiming.Build(line, nextTime);

            var span = Math.Max(400, (nextTime - line.Time).TotalMilliseconds);
            var position = line.Time.TotalMilliseconds + span * fraction;

            var history = new List<string>();
            for (var i = Math.Max(0, index - 2); i < index; i++)
            {
                if (!string.IsNullOrWhiteSpace(document.Lines[i].Text))
                {
                    history.Add(document.Lines[i].Text);
                }
            }

            return new Frame(words, line.Text, index, history, position);
        }

        return Synthetic(SampleLine, explicitHistory, fraction);
    }

    private static Frame Synthetic(string text, string[]? history, double fraction)
    {
        var line = new LyricLine(TimeSpan.Zero, text);
        var words = WordTiming.Build(line, TimeSpan.FromSeconds(5));
        var position = words.Count == 0
            ? 0
            : words[Math.Clamp((int)(words.Count * fraction), 0, words.Count - 1)].StartMs + 40;

        return new Frame(words, text, history?.Length ?? 0, history ?? Array.Empty<string>(), position);
    }

    private static void Draw(SKCanvas canvas, SKImageInfo info, OverlayStyle style, StyleKind kind,
        SKTypeface regular, SKTypeface bold, SKTypeface mono,
        PlaybackSnapshot? snapshot, LyricsResult? lyrics, Frame frame,
        double positionMs, double durationMs, float enter, float alpha, bool playing, bool fullScreen)
    {
        float width = info.Width;
        float height = info.Height;
        var stageHeight = Math.Min(OverlayLayout.StageHeight, height);

        regular = TextFonts.ForText(regular, frame.LineText);
        bold = TextFonts.ForText(bold, frame.LineText);
        mono = TextFonts.ForText(mono, frame.LineText);

        // The title is a full-screen-only element, pinned to the top-left of the frame.
        if (fullScreen)
        {
            var title = snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Title)
                ? snapshot.Title
                : "Preview";
            var artist = snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Title)
                ? snapshot.Artist
                : "no media session";
            KaraokeRenderer.DrawHeader(canvas, style, bold, title, artist, width, height, alpha, leftAligned: true);
        }

        // Kinetic Scatter composes over the whole surface, exactly as the live overlay does.
        if (kind == StyleKind.KineticScatter)
        {
            ScatterRenderer.Draw(canvas, style, bold, frame.Words, positionMs, frame.LineIndex,
                frame.History, width, height, alpha, playing);
            KaraokeRenderer.DrawProgress(canvas, style, width, height, positionMs, durationMs, alpha);
            return;
        }

        canvas.Save();
        if (fullScreen)
        {
            var logicalWidth = width / OverlayLayout.FullScreenZoom;
            canvas.Translate(width / 2f, height / 2f);
            canvas.Scale(OverlayLayout.FullScreenZoom, OverlayLayout.FullScreenZoom);
            canvas.Translate(-logicalWidth / 2f, -stageHeight / 2f);
            width = logicalWidth;
        }

        height = stageHeight;

        var context = new StyleContext(
            canvas,
            style,
            regular,
            bold,
            mono,
            frame.Words,
            positionMs,
            width,
            height,
            enter,
            alpha,
            playing,
            frame.History,
            frame.LineText,
            WordTiming.ContainsCjk(frame.LineText),
            frame.LineIndex);

        StyleRenderers.Draw(kind, context);

        if (kind is not (StyleKind.Bubble or StyleKind.NativeDesign) && enter < 1f && frame.History.Count > 0)
        {
            KaraokeRenderer.DrawSecondaryLine(canvas, style, regular, frame.History[^1],
                width, height * 0.30f, alpha * (1 - KaraokeRenderer.EaseOutCubic(enter)) * 0.5f, 0.9f);
        }

        canvas.Restore();
        KaraokeRenderer.DrawProgress(canvas, style, info.Width, info.Height, positionMs, durationMs, alpha);
    }

    private static SKColor ParseBackground(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new SKColor(14, 14, 18);
        }

        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase) || value == "none")
        {
            return SKColors.Transparent;
        }

        try
        {
            return SKColor.Parse(value);
        }
        catch
        {
            Log.Error($"unreadable --bg value '{value}', using default");
            return new SKColor(14, 14, 18);
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int GetIntArg(string[] args, string name, int fallback) =>
        int.TryParse(GetArg(args, name), out var value) && value > 0 ? value : fallback;

    private static double GetDoubleArg(string[] args, string name, double fallback) =>
        double.TryParse(GetArg(args, name), out var value) ? value : fallback;
}
