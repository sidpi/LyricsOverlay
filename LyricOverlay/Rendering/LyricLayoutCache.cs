using System;
using System.Collections.Generic;
using SkiaSharp;
using LyricOverlay.Lyrics;

namespace LyricOverlay.Rendering;

/// <summary>One line measured and fitted for a surface: everything the karaoke painter needs.</summary>
internal sealed class LineLayout
{
    public required float Size { get; init; }
    public required float Space { get; init; }
    public required float LineWidth { get; init; }
    public required float[] Widths { get; init; }
    public required SKFontMetrics Metrics { get; init; }
}

/// <summary>
/// Remembers the last line's layout. <see cref="KaraokeRenderer.DrawKaraokeLine"/> used to
/// measure every word twice on every frame (once to fit the line, once for the per-word advance),
/// which is a lot of text shaping for a result that cannot change until the line, font, scale or
/// surface width does. One entry is all the overlay draws per frame, so one entry is all it keeps.
/// </summary>
internal static class LyricLayoutCache
{
    private static readonly object Gate = new();
    private static ulong _key;
    private static bool _hasLayout;
    private static LineLayout? _layout;

    public static LineLayout Get(SKTypeface typeface, IReadOnlyList<TimedWord> words, float baseSize, float maxWidth)
    {
        var key = Key(typeface, words, baseSize, maxWidth);

        lock (Gate)
        {
            if (_hasLayout && _layout is not null && key == _key)
            {
                return _layout;
            }
        }

        var layout = Build(typeface, words, baseSize, maxWidth);

        lock (Gate)
        {
            _key = key;
            _hasLayout = true;
            _layout = layout;
        }

        return layout;
    }

    private static LineLayout Build(SKTypeface typeface, IReadOnlyList<TimedWord> words, float baseSize, float maxWidth)
    {
        // Fitting pass, at the base size: the fitted size is this ratio away from it.
        var measureFont = RenderCache.Font(typeface, baseSize, subpixelEdging: false);
        var spaceEstimate = measureFont.MeasureText(" ");
        var totalEstimate = 0f;
        for (var i = 0; i < words.Count; i++)
        {
            totalEstimate += measureFont.MeasureText(words[i].Text) + (i > 0 && !words[i].Glued ? spaceEstimate : 0f);
        }

        var fit = totalEstimate > maxWidth && totalEstimate > 0f ? maxWidth / totalEstimate : 1f;
        var size = Math.Max(18f, baseSize * fit);

        // Advance widths at the fitted size, which is what the per-word loop actually draws at.
        var font = RenderCache.Font(typeface, size);
        var space = font.MeasureText(" ");
        var widths = new float[words.Count];
        var lineWidth = 0f;
        for (var i = 0; i < words.Count; i++)
        {
            widths[i] = font.MeasureText(words[i].Text);
            lineWidth += widths[i] + (i > 0 && !words[i].Glued ? space : 0f);
        }

        return new LineLayout
        {
            Size = size,
            Space = space,
            LineWidth = lineWidth,
            Widths = widths,
            Metrics = font.Metrics,
        };
    }

    private static ulong Key(SKTypeface typeface, IReadOnlyList<TimedWord> words, float baseSize, float maxWidth)
    {
        var hash = 1469598103934665603UL;

        foreach (var ch in typeface.FamilyName ?? string.Empty)
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        var style = typeface.FontStyle;
        hash = (hash ^ (ulong)(int)style.Weight) * 1099511628211UL;
        hash = (hash ^ (ulong)(int)style.Slant) * 1099511628211UL;

        foreach (var word in words)
        {
            foreach (var ch in word.Text)
            {
                hash = (hash ^ ch) * 1099511628211UL;
            }

            // A glued word has no space before it, so the gap is part of the layout's identity.
            hash = (hash ^ (word.Glued ? 1UL : 2UL)) * 1099511628211UL;
        }

        hash = (hash ^ (ulong)MathF.Round(baseSize * 64f)) * 1099511628211UL;
        hash = (hash ^ (ulong)MathF.Round(maxWidth * 8f)) * 1099511628211UL;
        return hash;
    }
}
