using System;
using System.Collections.Generic;
using SkiaSharp;
using LyricOverlay.Lyrics;

namespace LyricOverlay.Rendering;

/// <summary>
/// Skia lyrics painter: three-tone word-level karaoke fill with a glow that hugs the sung
/// portion of each word, eased line transitions, and dimmed secondary lines.
/// </summary>
internal static class KaraokeRenderer
{
    private const float LineBaseSize = 44f;
    private const float SecondaryBaseSize = 21f;

    public static void DrawHeader(SKCanvas canvas, OverlayStyle style, SKTypeface typeface,
        string title, string artist, float width, float height, float alpha, bool leftAligned = false)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(artist) ? title : $"{title}  ·  {artist}";
        var scale = (float)style.TextScale;

        if (!leftAligned)
        {
            DrawCentered(canvas, text, typeface, 14 * scale, width / 2f, height * 0.11f,
                WithAlpha(style.Secondary, alpha * 0.85f));
            return;
        }

        // Full-screen header: pinned to the top-left, shrunk rather than clipped for long titles.
        var size = 14f * scale;
        using (var measure = new SKFont(typeface, size) { Subpixel = true })
        {
            var measured = measure.MeasureText(text);
            var maxWidth = width * 0.55f;
            if (measured > maxWidth && measured > 0)
            {
                size *= maxWidth / measured;
            }
        }

        using var font = new SKFont(typeface, Math.Max(8f, size))
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias,
        };
        using var paint = new SKPaint { IsAntialias = true, Color = WithAlpha(style.Secondary, alpha * 0.85f) };
        var metrics = font.Metrics;
        // Pinned to top: 2.5% down, left: 3.5% in, measured against the live viewport.
        var baseline = height * 0.025f + (metrics.Descent - metrics.Ascent);
        canvas.DrawText(text, width * 0.035f, baseline, SKTextAlign.Left, font, paint);
    }

    /// <summary>Draws the active line with a per-word karaoke fill.</summary>
    public static void DrawKaraokeLine(SKCanvas canvas, OverlayStyle style, SKTypeface typeface,
        IReadOnlyList<TimedWord> words, double positionMs, float width, float centerY,
        float enterProgress, float alpha, bool playing, bool compact = false)
    {
        if (words.Count == 0)
        {
            return;
        }

        var ease = EaseOutCubic(enterProgress);
        var translateY = (1 - ease) * 14f;
        var lineAlpha = ease * alpha * (playing ? 1f : 0.62f);

        var baseSize = LineBaseSize * (float)style.TextScale;

        using var measureFont = new SKFont(typeface, baseSize) { Subpixel = true };
        var spaceEstimate = measureFont.MeasureText(" ");
        var totalEstimate = 0f;
        for (var i = 0; i < words.Count; i++)
        {
            totalEstimate += measureFont.MeasureText(words[i].Text) + (i > 0 && !words[i].Glued ? spaceEstimate : 0);
        }

        var maxWidth = width * 0.94f;
        var fit = totalEstimate > maxWidth && totalEstimate > 0 ? maxWidth / totalEstimate : 1f;
        var size = Math.Max(18f, baseSize * fit);

        using var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };

        // Blurred passes use grayscale AA: subpixel-positioned glyphs + a mask filter land a
        // fraction of a pixel off, which reads as the glow sitting left/below the letters.
        using var glowFont = new SKFont(typeface, size) { Subpixel = false, Edging = SKFontEdging.Antialias };
        var metrics = font.Metrics;
        var wordSpace = font.MeasureText(" ");

        var widths = new float[words.Count];
        var lineWidth = 0f;
        for (var i = 0; i < words.Count; i++)
        {
            widths[i] = font.MeasureText(words[i].Text);
            lineWidth += widths[i] + (i > 0 && !words[i].Glued ? wordSpace : 0);
        }

        // Padding covers side bearings and descenders so no letter gets clipped.
        var pad = Math.Max(3f, size * 0.16f);
        var blur = Math.Max(4f, size * 0.20f);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 150),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur * 0.5f),
        };
        using var basePaint = new SKPaint { IsAntialias = true };
        using var highlightPaint = new SKPaint { IsAntialias = true };
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = style.Glow ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur) : null,
        };

        var baseline = centerY + translateY - (metrics.Ascent + metrics.Descent) / 2f;
        var x = width / 2f - lineWidth / 2f;
        var activeIndex = ActiveWordIndex(words, positionMs);

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var fill = (float)word.FillAt(positionMs);
            var isSung = fill >= 1f;
            var isActive = i == activeIndex;

            var wordWidth = widths[i];
            var pop = isActive && fill > 0f && fill < 1f
                ? 1f + 0.035f * (float)Math.Sin(fill * Math.PI)
                : 1f;

            var centerX = x + wordWidth / 2f;
            var centerYWord = baseline - size * 0.35f;

            canvas.Save();
            if (pop > 1.001f)
            {
                canvas.Translate(centerX, centerYWord);
                canvas.Scale(pop, pop);
                canvas.Translate(-centerX, -centerYWord);
            }

            // Soft shadow keeps text readable over bright video. Drawn on the same baseline:
            // any vertical offset shows up as a doubled, misaligned halo.
            canvas.DrawText(word.Text, x, baseline, SKTextAlign.Left, font, shadowPaint);

            var baseAlpha = isSung
                ? 0.70f
                : isActive
                    ? 0.55f
                    : 0.32f + 0.26f * Aheadness(words, i, positionMs);
            basePaint.Color = WithAlpha(style.Text, lineAlpha * baseAlpha);
            canvas.DrawText(word.Text, x, baseline, SKTextAlign.Left, font, basePaint);

            if (fill > 0f)
            {
                // The glow needs room for the blur to spread; clipping it to the advance box
                // sheared the halo off at the edges. Pad generously and snap to whole pixels.
                var glowPad = blur * 2f + 2f;
                var glowClip = new SKRect(
                    MathF.Floor(x - glowPad),
                    MathF.Floor(baseline + metrics.Ascent - glowPad),
                    MathF.Ceiling(x + wordWidth * fill + (fill >= 1f ? glowPad : glowPad * 0.5f)),
                    MathF.Ceiling(baseline + metrics.Descent + glowPad));

                if (style.Glow)
                {
                    var intensity = GlowIntensity(word, positionMs);
                    if (intensity > 0.01f)
                    {
                        glowPaint.Color = WithAlpha(style.GlowTint, lineAlpha * intensity);
                        canvas.Save();
                        canvas.ClipRect(glowClip);
                        canvas.DrawText(word.Text, x, baseline, SKTextAlign.Left, glowFont, glowPaint);
                        canvas.Restore();
                    }
                }

                var fillClip = new SKRect(
                    MathF.Floor(x - pad),
                    MathF.Floor(baseline + metrics.Ascent - pad),
                    MathF.Ceiling(x + wordWidth * fill + pad * (fill >= 1f ? 1f : 0.5f)),
                    MathF.Ceiling(baseline + metrics.Descent + pad));

                highlightPaint.Color = WithAlpha(style.Highlight, lineAlpha * 0.96f);
                canvas.Save();
                canvas.ClipRect(fillClip);
                canvas.DrawText(word.Text, x, baseline, SKTextAlign.Left, font, highlightPaint);
                canvas.Restore();
            }

            canvas.Restore();
            x += wordWidth + (i + 1 < words.Count && !words[i + 1].Glued ? wordSpace : 0);
        }
    }

    /// <summary>
    /// Glow strength for one word: steady while it is being sung, then a short decaying tail,
    /// so the glow covers the word fully instead of vanishing at completion.
    /// </summary>
    private static float GlowIntensity(TimedWord word, double positionMs)
    {
        if (positionMs < word.StartMs)
        {
            return 0f;
        }

        if (positionMs <= word.EndMs)
        {
            return 0.72f + 0.12f * (float)Math.Sin(positionMs / 90.0);
        }

        var sinceEnd = positionMs - word.EndMs;
        return Math.Max(0f, 0.72f * (1f - (float)(sinceEnd / 500.0)));
    }

    private static int ActiveWordIndex(IReadOnlyList<TimedWord> words, double positionMs)
    {
        var active = -1;
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i].StartMs <= positionMs)
            {
                active = i;
            }
            else
            {
                break;
            }
        }

        return active;
    }

    public static void DrawSecondaryLine(SKCanvas canvas, OverlayStyle style, SKTypeface typeface,
        string? text, float width, float centerY, float alpha, float sizeFactor = 1f)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var baseSize = SecondaryBaseSize * (float)style.TextScale * sizeFactor;
        using var measureFont = new SKFont(typeface, baseSize) { Subpixel = true };
        var measured = measureFont.MeasureText(text);
        var maxWidth = width * 0.94f;
        var size = measured > maxWidth && measured > 0 ? baseSize * maxWidth / measured : baseSize;

        DrawCentered(canvas, text, typeface, size, width / 2f, centerY, WithAlpha(style.Secondary, alpha));
    }

    public static void DrawProgress(SKCanvas canvas, OverlayStyle style, float width, float height,
        double positionMs, double durationMs, float alpha)
    {
        if (durationMs <= 0)
        {
            return;
        }

        var fraction = Math.Clamp((float)(positionMs / durationMs), 0f, 1f);
        // Pinned across the viewport: 3.5% in from each side, 3.5% up from the bottom edge.
        var x = width * 0.035f;
        var barWidth = width - x * 2f;
        var y = height - Math.Max(8f, height * 0.035f);

        using var track = new SKPaint { IsAntialias = true, Color = WithAlpha(style.Text, alpha * 0.10f) };
        using var fill = new SKPaint { IsAntialias = true, Color = WithAlpha(style.Highlight, alpha * 0.50f) };

        canvas.DrawRoundRect(new SKRect(x, y, x + barWidth, y + 2f), 1f, 1f, track);
        canvas.DrawRoundRect(new SKRect(x, y, x + barWidth * fraction, y + 2f), 1f, 1f, fill);
    }

    /// <summary>0 for far-future words, 1 for the word about to be sung.</summary>
    private static float Aheadness(IReadOnlyList<TimedWord> words, int index, double positionMs)
    {
        var word = words[index];
        if (word.StartMs <= positionMs)
        {
            return 1f;
        }

        return (float)Math.Clamp(1.0 - (word.StartMs - positionMs) / 2500.0, 0, 1);
    }

    private static void DrawCentered(SKCanvas canvas, string text, SKTypeface typeface, float size,
        float x, float centerY, SKColor color)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var font = new SKFont(typeface, Math.Max(8f, size)) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { IsAntialias = true, Color = color };

        var metrics = font.Metrics;
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, paint);
    }

    public static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0, 1);
        var inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }

    private static SKColor WithAlpha(SKColor color, float alpha) =>
        color.WithAlpha((byte)Math.Clamp(color.Alpha * alpha, 0, 255));
}
