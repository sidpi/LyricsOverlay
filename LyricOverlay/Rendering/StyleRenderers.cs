using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using LyricOverlay.Lyrics;

namespace LyricOverlay.Rendering;

/// <summary>Everything a style needs to paint one frame.</summary>
internal readonly record struct StyleContext(
    SKCanvas Canvas,
    OverlayStyle Style,
    SKTypeface Regular,
    SKTypeface Bold,
    SKTypeface Mono,
    IReadOnlyList<TimedWord> Words,
    double PositionMs,
    float Width,
    float Height,
    float Enter,
    float Alpha,
    bool Playing,
    IReadOnlyList<string> History,
    IReadOnlyList<string> Upcoming,
    string LineText,
    bool Compact,
    int LineIndex);

/// <summary>
/// Style dispatch. Every style reads the same word timings; only the arrival differs.
/// </summary>
internal static class StyleRenderers
{
    public static void Draw(StyleKind kind, in StyleContext ctx)
    {
        switch (kind)
        {
            case StyleKind.Fisheye:
                DrawFisheye(ctx);
                break;
            case StyleKind.Codestyle:
                DrawCodestyle(ctx);
                break;
            case StyleKind.Branches:
                DrawBranches(ctx);
                break;
            case StyleKind.Constellation:
                DrawConstellation(ctx);
                break;
            case StyleKind.Bubble:
                DrawBubble(ctx);
                break;
            case StyleKind.NativeDesign:
                DrawNativeDesign(ctx);
                break;
            case StyleKind.KineticScatter:
                ScatterRenderer.Draw(ctx);
                break;
            default:
                DrawKinetic(ctx);
                break;
        }
    }

    private static void DrawKinetic(in StyleContext ctx)
    {
        KaraokeRenderer.DrawKaraokeLine(ctx.Canvas, ctx.Style, ctx.Bold, ctx.Words, ctx.PositionMs,
            ctx.Width, ctx.Height * 0.58f, ctx.Enter, ctx.Alpha, ctx.Playing);
    }

    /// <summary>A tight three-word stack bulging through a lens.</summary>
    private static void DrawFisheye(in StyleContext ctx)
    {
        var active = ActiveIndex(ctx.Words, ctx.PositionMs);
        if (active < 0)
        {
            return;
        }

        var ease = KaraokeRenderer.EaseOutCubic(ctx.Enter);
        var baseAlpha = ctx.Alpha * ease * (ctx.Playing ? 1f : 0.62f);

        for (var offset = -1; offset <= 1; offset++)
        {
            var index = active + offset;
            if (index < 0 || index >= ctx.Words.Count)
            {
                continue;
            }

            var word = ctx.Words[index];
            var lens = (float)(1.0 + 0.50 * Math.Exp(-Math.Abs(offset) * 1.5));
            var size = Math.Max(14f, 28f * (float)ctx.Style.TextScale) * lens;
            var centerY = ctx.Height * 0.58f + offset * 44f;
            var fill = (float)word.FillAt(ctx.PositionMs);

            var color = offset == 0 ? ctx.Style.Highlight : ctx.Style.Text;
            var alpha = baseAlpha * (offset == 0 ? 1f : 0.45f);

            DrawWordWithFill(ctx.Canvas, ctx.Bold, word.Text, size, ctx.Width / 2f, centerY,
                color, ctx.Style.GlowTint, alpha, offset == 0 ? fill : (offset < 0 ? 1f : 0f), ctx.Style.Glow);
        }
    }

    /// <summary>
    /// A shell transcript: the line just sung stays above as a muted comment, and the current
    /// line follows a red prompt, filling in as it is sung.
    /// </summary>
    private static void DrawCodestyle(in StyleContext ctx)
    {
        var previous = ctx.History.LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
        var text = ctx.LineText;

        if (previous is null && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var ease = KaraokeRenderer.EaseOutCubic(ctx.Enter);
        var scale = (float)ctx.Style.TextScale;
        var activeSize = Math.Max(15f, 26f * scale);
        var commentSize = Math.Max(9f, activeSize * 0.60f);

        var left = ctx.Width * 0.09f;
        var maxWidth = ctx.Width * 0.82f;

        // The block sits in the lower half: the comment above, the active line, queued lines below.
        var upcoming = ctx.Upcoming;
        var lift = (1f - ease) * activeSize * 0.55f;
        var activeY = ctx.Height * (upcoming.Count > 0 ? 0.60f : 0.67f) + lift;
        var commentY = Math.Max(ctx.Height * 0.24f, activeY - activeSize * 1.35f) + lift * 0.4f;

        // Queued rows share the room between the active line and the progress bar.
        var upcomingGap = activeSize * 1.15f;
        if (upcoming.Count > 0)
        {
            var room = ctx.Height * 0.86f - activeY;
            upcomingGap = Math.Min(upcomingGap, Math.Max(activeSize * 0.75f, room / upcoming.Count));
        }

        var lavender = new SKColor(0x9B, 0xB1, 0xFF);
        var promptRed = new SKColor(0xFF, 0x3B, 0x30);

        using var promptPaint = new SKPaint { IsAntialias = true };
        using var commentPaint = new SKPaint { IsAntialias = true, MaskFilter = RenderCache.Blur(1.4f) };
        using var ghostPaint = new SKPaint { IsAntialias = true };
        using var litPaint = new SKPaint { IsAntialias = true };
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = ctx.Style.Glow ? RenderCache.Blur(9f) : null,
        };

        // Previous line, as a comment in the same face: smaller, dim, and slightly out of focus.
        if (previous is not null)
        {
            var commentFont = RenderCache.Font(ctx.Mono, commentSize);
            var commentMetrics = commentFont.Metrics;
            const string prefix = "// ";
            var body = previous.Trim();
            var rowWidth = commentFont.MeasureText(prefix) + commentFont.MeasureText(body);
            var fit = rowWidth > maxWidth && rowWidth > 0 ? maxWidth / rowWidth : 1f;
            var baseline = commentY - (commentMetrics.Ascent + commentMetrics.Descent) / 2f;

            ctx.Canvas.Save();
            if (fit < 1f)
            {
                ctx.Canvas.Translate(left, baseline);
                ctx.Canvas.Scale(fit, fit);
                ctx.Canvas.Translate(-left, -baseline);
            }

            commentPaint.Color = WithAlpha(lavender, ctx.Alpha * ease * 0.52f);
            ctx.Canvas.DrawText(prefix, left, baseline, SKTextAlign.Left, commentFont, commentPaint);
            commentPaint.Color = WithAlpha(lavender, ctx.Alpha * ease * 0.34f);
            ctx.Canvas.DrawText(body, left + commentFont.MeasureText(prefix), baseline,
                SKTextAlign.Left, commentFont, commentPaint);
            ctx.Canvas.Restore();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var font = RenderCache.Font(ctx.Mono, activeSize);
        var metrics = font.Metrics;
        const string prompt = ">";
        var textX = left + font.MeasureText(prompt) + activeSize * 0.60f;
        var fullWidth = textX - left + font.MeasureText(text);
        var rowFit = fullWidth > maxWidth && fullWidth > 0 ? maxWidth / fullWidth : 1f;
        var rowBaseline = activeY - (metrics.Ascent + metrics.Descent) / 2f;

        ctx.Canvas.Save();
        if (rowFit < 1f)
        {
            ctx.Canvas.Translate(left, rowBaseline);
            ctx.Canvas.Scale(rowFit, rowFit);
            ctx.Canvas.Translate(-left, -rowBaseline);
        }

        promptPaint.Color = WithAlpha(promptRed, ctx.Alpha * ease);
        ctx.Canvas.DrawText(prompt, left, rowBaseline, SKTextAlign.Left, font, promptPaint);

        // The unsung remainder stays quiet, so the lit part reads as a live reveal.
        ghostPaint.Color = WithAlpha(lavender, ctx.Alpha * ease * 0.30f);
        ctx.Canvas.DrawText(text, textX, rowBaseline, SKTextAlign.Left, font, ghostPaint);

        var revealed = SungFraction(ctx) * text.Length;
        var whole = Math.Clamp((int)Math.Floor(revealed), 0, text.Length);
        var fraction = revealed - whole;
        var litWidth = font.MeasureText(text[..whole]);

        if (whole > 0)
        {
            if (ctx.Style.Glow)
            {
                glowPaint.Color = WithAlpha(lavender, ctx.Alpha * ease * 0.50f);
                DrawTextUpTo(ctx.Canvas, text, textX, rowBaseline, font, glowPaint, litWidth, 10f);
            }

            litPaint.Color = WithAlpha(lavender, ctx.Alpha * ease);
            DrawTextUpTo(ctx.Canvas, text, textX, rowBaseline, font, litPaint, litWidth, 2f);
        }

        // A partly sung letter brightens smoothly instead of popping in whole.
        if (whole < text.Length && fraction > 0.01f)
        {
            var partial = font.MeasureText(text.Substring(whole, 1)) * fraction;
            litPaint.Color = WithAlpha(lavender, ctx.Alpha * ease);
            ctx.Canvas.Save();
            ctx.Canvas.ClipRect(new SKRect(textX + litWidth, rowBaseline + metrics.Ascent - 1f,
                textX + litWidth + partial, rowBaseline + metrics.Descent + 1f));
            ctx.Canvas.DrawText(text, textX, rowBaseline, SKTextAlign.Left, font, litPaint);
            ctx.Canvas.Restore();
        }

        // Queued lines: same text column, smaller and dimmer, each behind its own muted prompt.
        for (var i = 0; i < upcoming.Count; i++)
        {
            var size = activeSize * (i == 0 ? 0.72f : 0.60f);
            var queuedFont = RenderCache.Font(ctx.Mono, size);
            var queuedMetrics = queuedFont.Metrics;
            var body = upcoming[i].Trim();
            var baseline = activeY + (i + 1) * upcomingGap - (queuedMetrics.Ascent + queuedMetrics.Descent) / 2f;
            var rowWidth = textX - left + queuedFont.MeasureText(body);
            var fit = rowWidth > maxWidth && rowWidth > 0 ? maxWidth / rowWidth : 1f;
            var queuedAlpha = ctx.Alpha * ease * (i == 0 ? 0.26f : 0.16f);

            ctx.Canvas.Save();
            if (fit < 1f)
            {
                ctx.Canvas.Translate(left, baseline);
                ctx.Canvas.Scale(fit, fit);
                ctx.Canvas.Translate(-left, -baseline);
            }

            promptPaint.Color = WithAlpha(lavender, queuedAlpha * 1.35f);
            ctx.Canvas.DrawText(prompt, textX - activeSize * 0.45f - queuedFont.MeasureText(prompt), baseline,
                SKTextAlign.Left, queuedFont, promptPaint);
            ghostPaint.Color = WithAlpha(lavender, queuedAlpha);
            ctx.Canvas.DrawText(body, textX, baseline, SKTextAlign.Left, queuedFont, ghostPaint);
            ctx.Canvas.Restore();
        }

        ctx.Canvas.Restore();
    }

    /// <summary>
    /// A vine: a wavy stem whose glowing tip travels with the voice, a curved tendril to
    /// every word that grows as the tip reaches it, and a bright ring on the sung node.
    /// </summary>
    private static void DrawBranches(in StyleContext ctx)
    {
        var count = ctx.Words.Count;
        if (count == 0)
        {
            return;
        }

        var ease = KaraokeRenderer.EaseOutCubic(ctx.Enter);
        var active = ActiveIndex(ctx.Words, ctx.PositionMs);
        var scale = (float)ctx.Style.TextScale;

        var left = ctx.Width * 0.07f;
        var right = ctx.Width * 0.93f;
        var span = Math.Max(1f, right - left);
        var baseY = ctx.Height * 0.62f;
        var amplitude = Math.Min(22f, ctx.Height * 0.085f);
        var phase = (ctx.Words[0].StartMs % 1500) / 1500.0 * Math.PI * 2;

        float WaveY(float x) =>
            baseY + (float)Math.Sin((x - left) / span * Math.PI * 2.0 + phase) * amplitude;

        // Labels are measured first: their widths set the branch points along the stem.
        var size = Math.Max(11f, 14f * scale);
        var widths = MeasureWords(ctx.Bold, ctx.Words, size);
        var gap = 16f;
        var available = ctx.Width * 0.84f;
        var total = widths.Sum() + gap * (count - 1) + ActiveExtra(widths, active);

        if (total > available && total > 0)
        {
            var fit = available / total;
            size = Math.Max(9f, size * fit);
            gap = Math.Max(7f, gap * fit);
            widths = MeasureWords(ctx.Bold, ctx.Words, size);
            total = widths.Sum() + gap * (count - 1) + ActiveExtra(widths, active);
        }

        var nodes = new SKPoint[count];
        var tips = new SKPoint[count];
        var cursor = left + (span - total) / 2f;
        for (var i = 0; i < count; i++)
        {
            var nodeX = cursor + widths[i] / 2f;
            nodes[i] = new SKPoint(nodeX, WaveY(nodeX));

            var above = i % 2 == 0;
            var reach = 30f + 8f * (float)Math.Sin(i * 1.3);
            tips[i] = new SKPoint(nodeX + (above ? 4f : -4f), nodes[i].Y + (above ? -reach : reach));
            cursor += widths[i] + gap + (i == active ? ActiveExtra(widths, active) : 0f);
        }

        // The tip is the frontier: it rests on the sung node and slides toward the next word.
        var headX = left;
        if (active >= 0)
        {
            var fill = (float)ctx.Words[active].FillAt(ctx.PositionMs);
            var next = Math.Min(active + 1, count - 1);
            var target = nodes[active].X + (nodes[next].X - nodes[active].X) * Math.Clamp(fill, 0f, 1f);
            headX = left + (target - left) * ease;
        }

        headX = Math.Clamp(headX, left, right);
        var headY = WaveY(headX);

        const int samples = 110;
        var wave = new SKPoint[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            var x = left + span * i / samples;
            wave[i] = new SKPoint(x, WaveY(x));
        }

        using var stemDim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
            Color = WithAlpha(ctx.Style.Text, ctx.Alpha * 0.20f),
        };
        using var stemLit = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.4f,
            StrokeCap = SKStrokeCap.Round,
            Color = WithAlpha(ctx.Style.GlowTint, ctx.Alpha * 0.75f),
        };

        ctx.Canvas.DrawPoints(SKPointMode.Polygon, wave, stemDim);

        var litCount = (int)Math.Round(samples * (headX - left) / span);
        if (litCount > 0)
        {
            var lit = new SKPoint[litCount + 1];
            Array.Copy(wave, lit, litCount);
            lit[litCount] = new SKPoint(headX, headY);
            ctx.Canvas.DrawPoints(SKPointMode.Polygon, lit, stemLit);
        }

        using var tendril = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.1f,
            StrokeCap = SKStrokeCap.Round,
        };
        using var tendrilLit = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.8f,
            StrokeCap = SKStrokeCap.Round,
        };

        // How grown a tendril is: it starts just before the tip reaches its branch point.
        float Grow(int i) => Math.Clamp((headX - nodes[i].X + 26f) / 26f, 0f, 1f);

        for (var i = 0; i < count; i++)
        {
            var grow = Grow(i);
            if (grow <= 0.01f)
            {
                continue;
            }

            var above = i % 2 == 0;
            var bend = new SKPoint(nodes[i].X + (above ? 12f : -12f), (nodes[i].Y + tips[i].Y) / 2f);
            var curve = SampleQuadratic(nodes[i], bend, tips[i]);
            var upto = Math.Max(2, (int)Math.Round((curve.Length - 1) * grow));
            var grown = curve[..(upto + 1)];

            if (i == active)
            {
                tendrilLit.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * 0.75f);
                ctx.Canvas.DrawPoints(SKPointMode.Polygon, grown, tendrilLit);
            }
            else
            {
                var sung = ctx.Words[i].FillAt(ctx.PositionMs) > 0;
                tendril.Color = WithAlpha(ctx.Style.Text, ctx.Alpha * ease * (sung ? 0.34f : 0.18f));
                ctx.Canvas.DrawPoints(SKPointMode.Polygon, grown, tendril);
            }
        }

        using var dot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
        using var halo = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = RenderCache.Blur(6f),
        };

        // The travelling head: a soft white glow with a hard bright centre.
        if (headX > left + 0.5f)
        {
            halo.Color = WithAlpha(SKColors.White, ctx.Alpha * 0.55f);
            ctx.Canvas.DrawCircle(headX, headY, 12f + 2.5f * (float)Math.Sin(ctx.PositionMs / 180.0), halo);
            dot.Color = WithAlpha(SKColors.White, ctx.Alpha * 0.95f);
            ctx.Canvas.DrawCircle(headX, headY, 3.2f, dot);
        }

        for (var i = 0; i < count; i++)
        {
            var grow = Grow(i);
            if (grow <= 0.01f)
            {
                continue;
            }

            var fill = (float)ctx.Words[i].FillAt(ctx.PositionMs);

            if (i == active)
            {
                halo.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * grow * (0.30f + 0.35f * fill));
                ctx.Canvas.DrawCircle(nodes[i], 10f, halo);
                ring.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * grow);
                ctx.Canvas.DrawCircle(nodes[i], 6.5f, ring);
                dot.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * grow);
                ctx.Canvas.DrawCircle(nodes[i], 2.4f, dot);
            }
            else
            {
                dot.Color = WithAlpha(ctx.Style.Text, ctx.Alpha * ease * grow * (fill > 0 ? 0.60f : 0.36f));
                ctx.Canvas.DrawCircle(nodes[i], fill > 0 ? 2.4f : 2f, dot);
            }
        }

        using var label = new SKPaint { IsAntialias = true };
        using var labelGlow = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = ctx.Style.Glow ? RenderCache.Blur(6f) : null,
        };

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var isActive = i == active;
                if ((pass == 0) == isActive)
                {
                    continue;
                }

                var grow = Grow(i);
                if (grow < 0.35f && !isActive)
                {
                    continue;
                }

                var fontSize = isActive ? size * 1.40f : size;
                var font = RenderCache.Font(ctx.Bold, fontSize);
                var half = font.MeasureText(ctx.Words[i].Text) / 2f + 8f;
                var labelX = Math.Clamp(tips[i].X, half, ctx.Width - half);
                var baseline = Math.Clamp(tips[i].Y + fontSize * 0.35f, fontSize + 4f, ctx.Height - 8f);

                if (isActive)
                {
                    labelGlow.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * grow * 0.55f);
                    ctx.Canvas.DrawText(ctx.Words[i].Text, labelX, baseline, SKTextAlign.Center, font, labelGlow);
                    label.Color = WithAlpha(ctx.Style.Highlight, ctx.Alpha * ease * grow);
                }
                else
                {
                    var sung = ctx.Words[i].FillAt(ctx.PositionMs) > 0;
                    label.Color = WithAlpha(ctx.Style.Text, ctx.Alpha * ease * grow * (sung ? 0.55f : 0.30f));
                }

                ctx.Canvas.DrawText(ctx.Words[i].Text, labelX, baseline, SKTextAlign.Center, font, label);
            }
        }
    }

    /// <summary>
    /// Words as stars in a loose sky. Each node and its ray arrive with its word, so the
    /// constellation builds in time with the voice, and the sung star flares white.
    /// </summary>
    private static void DrawConstellation(in StyleContext ctx)
    {
        var count = ctx.Words.Count;
        if (count == 0)
        {
            return;
        }

        var ease = KaraokeRenderer.EaseOutCubic(ctx.Enter);
        var active = ActiveIndex(ctx.Words, ctx.PositionMs);
        var scale = (float)ctx.Style.TextScale;
        var seed = (ctx.Words[0].StartMs % 89) / 89.0;

        var left = ctx.Width * 0.10f;
        var right = ctx.Width * 0.90f;
        var step = count > 1 ? (right - left) / (count - 1) : 0f;

        // Two wobble frequencies, seeded per line, so each line sits as its own little sky.
        var points = new SKPoint[count];
        for (var i = 0; i < count; i++)
        {
            var wobble = (float)(Math.Sin(i * 1.7 + seed * 6.28) * ctx.Height * 0.12 +
                                 Math.Sin(i * 0.62 + seed * 3.1) * ctx.Height * 0.05);
            points[i] = new SKPoint(count > 1 ? left + step * i : ctx.Width / 2f, ctx.Height * 0.56f + wobble);
        }

        // A node is invisible until its word is reached, then fades and pops into place.
        var appear = new float[count];
        for (var i = 0; i < count; i++)
        {
            appear[i] = i <= active
                ? (float)Math.Clamp((ctx.PositionMs - ctx.Words[i].StartMs) / 220.0, 0, 1)
                : 0f;
        }

        using var rayDim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };
        using var rayLit = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
        };

        // Rays arrive with the later of their two stars, then light as that word is sung.
        for (var i = 1; i < count; i++)
        {
            if (appear[i] <= 0.01f)
            {
                continue;
            }

            rayDim.Color = WithAlpha(ctx.Style.Text, ctx.Alpha * ease * 0.18f * appear[i]);
            ctx.Canvas.DrawLine(points[i - 1], points[i], rayDim);

            if (ctx.Words[i].FillAt(ctx.PositionMs) > 0)
            {
                rayLit.Color = WithAlpha(ctx.Style.Highlight, ctx.Alpha * ease * 0.40f * appear[i]);
                ctx.Canvas.DrawLine(points[i - 1], points[i], rayLit);
            }
        }

        var size = Math.Max(10f, 13f * scale);
        var widths = MeasureWords(ctx.Bold, ctx.Words, size);
        var total = widths.Sum() + 18f * (count - 1);

        // A line of twenty words cannot label every node legibly; near the singer it can.
        var showAll = total <= ctx.Width * 0.86f;

        using var star = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var halo = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = RenderCache.Blur(7f),
        };
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };

        // Inactive stars, then the active one, so its flare is never covered by a neighbour.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var isActive = i == active;
                if ((pass == 0) == isActive || appear[i] <= 0.01f)
                {
                    continue;
                }

                var fill = (float)ctx.Words[i].FillAt(ctx.PositionMs);
                var pop = 0.55f + 0.45f * appear[i];

                if (isActive)
                {
                    var flare = 1f + 0.22f * fill;
                    halo.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * appear[i] * 0.55f);
                    ctx.Canvas.DrawCircle(points[i], 14f * flare, halo);
                    ring.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * appear[i] * 0.90f);
                    ctx.Canvas.DrawCircle(points[i], 11f * flare, ring);
                    star.Color = WithAlpha(SKColors.White, ctx.Alpha * ease * appear[i]);
                    DrawStar4(ctx.Canvas, points[i], 9.5f * flare, 2.6f * flare, star);
                }
                else
                {
                    star.Color = WithAlpha(ctx.Style.Text, ctx.Alpha * ease * appear[i] * (fill >= 1f ? 0.60f : 0.45f));
                    DrawStar4(ctx.Canvas, points[i], (fill >= 1f ? 5f : 3.8f) * pop, 1.3f * pop, star);
                }
            }
        }

        using var label = new SKPaint { IsAntialias = true };
        using var labelGlow = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = ctx.Style.Glow ? RenderCache.Blur(6f) : null,
        };

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var isActive = i == active;
                if ((pass == 0) == isActive || appear[i] <= 0.01f)
                {
                    continue;
                }

                if (!showAll && !isActive && Math.Abs(i - active) > 2)
                {
                    continue; // long line: only the words around the singer get a label
                }

                var text = ctx.Words[i].Text;
                var fill = (float)ctx.Words[i].FillAt(ctx.PositionMs);
                var fontSize = isActive ? size * 1.6f : size;
                var font = RenderCache.Font(ctx.Bold, fontSize);

                var half = widths[i] / 2f + 10f;
                var labelX = Math.Clamp(points[i].X, half, ctx.Width - half);

                // Labels sit above their star with room for ascenders and descenders.
                var baseline = Math.Clamp(points[i].Y - 18f - (isActive ? 4f : 0f),
                    fontSize + 6f, ctx.Height - 10f);

                var alpha = ctx.Alpha * ease * appear[i];

                if (isActive)
                {
                    labelGlow.Color = WithAlpha(SKColors.White, alpha * 0.55f);
                    ctx.Canvas.DrawText(text, labelX, baseline, SKTextAlign.Center, font, labelGlow);
                    label.Color = WithAlpha(SKColors.White, alpha);
                }
                else
                {
                    label.Color = WithAlpha(ctx.Style.Text, alpha * (fill >= 1f ? 0.58f : 0.45f));
                }

                ctx.Canvas.DrawText(text, labelX, baseline, SKTextAlign.Center, font, label);
            }
        }
    }

    /// <summary>Recent lines as chat bubbles, newest filling word by word.</summary>
    private static void DrawBubble(in StyleContext ctx)
    {
        var ease = KaraokeRenderer.EaseOutCubic(ctx.Enter);
        var lines = ctx.History.TakeLast(2).Concat(new[] { ctx.LineText }).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var size = Math.Max(12f, 20f * (float)ctx.Style.TextScale);
        var font = RenderCache.Font(ctx.Bold, size, subpixelEdging: false);
        var metrics = font.Metrics;
        var bubbleHeight = size + 18f;

        using var bubblePaint = new SKPaint { IsAntialias = true };

        var bottom = ctx.Height * 0.74f;
        for (var i = 0; i < lines.Count; i++)
        {
            var isCurrent = i == lines.Count - 1;
            var text = lines[i];
            var textWidth = font.MeasureText(text);
            var maxWidth = ctx.Width * 0.78f;
            var fit = textWidth > maxWidth && textWidth > 0 ? maxWidth / textWidth : 1f;
            var width = Math.Min(textWidth * fit, maxWidth) + 28f;

            var y = bottom - (lines.Count - 1 - i) * (bubbleHeight + 8f);
            var rect = new SKRect(ctx.Width - 40f - width, y - bubbleHeight, ctx.Width - 40f, y);

            bubblePaint.Color = isCurrent
                ? WithAlpha(ctx.Style.Highlight, ctx.Alpha * 0.16f)
                : WithAlpha(ctx.Style.Text, ctx.Alpha * 0.08f);
            ctx.Canvas.DrawRoundRect(rect, bubbleHeight / 2f, bubbleHeight / 2f, bubblePaint);

            var baseline = rect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
            var textX = rect.Left + 14f;

            ctx.Canvas.Save();
            if (fit < 1f)
            {
                ctx.Canvas.Translate(textX, baseline);
                ctx.Canvas.Scale(fit, fit);
                ctx.Canvas.Translate(-textX, -baseline);
            }

            using var paint = new SKPaint { IsAntialias = true, Color = WithAlpha(ctx.Style.Text, ctx.Alpha * ease * (isCurrent ? 0.95f : 0.5f)) };
            ctx.Canvas.DrawText(text, textX, baseline, SKTextAlign.Left, font, paint);

            if (isCurrent && ctx.Words.Count > 0)
            {
                var revealed = 0;
                foreach (var word in ctx.Words)
                {
                    var fill = word.FillAt(ctx.PositionMs);
                    if (fill > 0)
                    {
                        revealed += (int)Math.Round(word.Text.Length * fill) + (word.Glued ? 0 : 1);
                    }
                }

                revealed = Math.Clamp(revealed, 0, text.Length);
                if (revealed > 0)
                {
                    using var fillPaint = new SKPaint { IsAntialias = true, Color = WithAlpha(ctx.Style.Highlight, ctx.Alpha * ease) };
                    ctx.Canvas.DrawText(text[..revealed], textX, baseline, SKTextAlign.Left, font, fillPaint);
                }
            }

            ctx.Canvas.Restore();
        }
    }



    /// <summary>
    /// The song as a native chat thread: dark charcoal bubbles on the left, vibrant blue ones
    /// on the right, the newest revealing its words left to right as they are sung.
    /// </summary>
    private static void DrawNativeDesign(in StyleContext ctx)
    {
        var lines = ctx.History.TakeLast(2).Concat(new[] { ctx.LineText })
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        var ease = KaraokeRenderer.EaseOutCubic(ctx.Enter);
        var scale = (float)ctx.Style.TextScale;
        var size = Math.Max(12f, 18f * scale);
        var font = RenderCache.Font(ctx.Bold, size);
        var metrics = font.Metrics;

        var padX = 16f;
        var padY = 11f;
        var bubbleHeight = size + padY * 2f;
        var radius = 18f;
        var gap = 10f;
        var maxWidth = ctx.Width * 0.62f;
        var margin = ctx.Width * 0.06f;

        // The oldest bubble stays below the track header, and the thread hugs the lower half.
        var slot = bubbleHeight + gap;
        var topMargin = ctx.Height * 0.20f;
        var bottom = Math.Min(ctx.Height - 34f,
            Math.Max(ctx.Height * 0.78f, topMargin + (lines.Count - 1) * slot + bubbleHeight));

        var charcoal = new SKColor(0x23, 0x25, 0x2B);
        var blue = new SKColor(0x2E, 0x6B, 0xFF);
        var textOnCharcoal = new SKColor(0xF2, 0xF4, 0xFA);
        var textOnBlue = new SKColor(0xFF, 0xFF, 0xFF);

        using var bubblePaint = new SKPaint { IsAntialias = true };
        using var tailPaint = new SKPaint { IsAntialias = true };
        using var bodyPaint = new SKPaint { IsAntialias = true };
        using var litPaint = new SKPaint { IsAntialias = true };

        for (var depth = 0; depth < lines.Count; depth++)
        {
            var index = lines.Count - 1 - depth;
            var isCurrent = depth == 0;
            var text = lines[index];
            var lineNumber = ctx.LineIndex - depth;
            var leftAligned = ((lineNumber % 2) + 2) % 2 == 0;

            var textWidth = font.MeasureText(text);
            var fit = textWidth > maxWidth && textWidth > 0 ? maxWidth / textWidth : 1f;
            var width = Math.Min(textWidth * fit, maxWidth) + padX * 2f;

            // The newest bubble rises into place while older ones settle upward.
            var lift = isCurrent ? (1f - ease) * 26f : (1f - ease) * 8f;
            var bubbleBottom = bottom - depth * (bubbleHeight + gap) + lift;
            var bubbleLeft = leftAligned ? margin : ctx.Width - margin - width;
            var rect = new SKRect(bubbleLeft, bubbleBottom - bubbleHeight, bubbleLeft + width, bubbleBottom);

            var alpha = ctx.Alpha * (isCurrent ? ease : 0.45f + 0.55f * ease);
            bubblePaint.Color = WithAlpha(leftAligned ? charcoal : blue, alpha * (isCurrent ? 0.95f : 0.78f));
            ctx.Canvas.DrawRoundRect(rect, radius, radius, bubblePaint);

            // Corner tail: a small wedge at the bottom of the bubble's own side.
            tailPaint.Color = bubblePaint.Color;
            var tail = leftAligned
                ? new[]
                {
                    new SKPoint(rect.Left + 6f, rect.Bottom - 2f),
                    new SKPoint(rect.Left - 7f, rect.Bottom + 9f),
                    new SKPoint(rect.Left + 24f, rect.Bottom - 1f),
                    new SKPoint(rect.Left + 6f, rect.Bottom - 2f),
                }
                : new[]
                {
                    new SKPoint(rect.Right - 6f, rect.Bottom - 2f),
                    new SKPoint(rect.Right + 7f, rect.Bottom + 9f),
                    new SKPoint(rect.Right - 24f, rect.Bottom - 1f),
                    new SKPoint(rect.Right - 6f, rect.Bottom - 2f),
                };
            ctx.Canvas.DrawPoints(SKPointMode.Polygon, tail, tailPaint);

            var baseline = rect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
            var textX = rect.Left + padX;
            var textColor = leftAligned ? textOnCharcoal : textOnBlue;

            ctx.Canvas.Save();
            if (fit < 1f)
            {
                ctx.Canvas.Translate(textX, baseline);
                ctx.Canvas.Scale(fit, fit);
                ctx.Canvas.Translate(-textX, -baseline);
            }

            // The unsung remainder of the newest bubble stays quiet, so the lit part reads as a reveal.
            bodyPaint.Color = WithAlpha(textColor, alpha * (isCurrent ? 0.38f : 0.55f));
            ctx.Canvas.DrawText(text, textX, baseline, SKTextAlign.Left, font, bodyPaint);

            if (isCurrent)
            {
                var revealed = SungFraction(ctx) * text.Length;
                var whole = Math.Clamp((int)Math.Floor(revealed), 0, text.Length);
                var fraction = revealed - whole;

                if (whole > 0)
                {
                    litPaint.Color = WithAlpha(textColor, alpha);
                    ctx.Canvas.DrawText(text[..whole], textX, baseline, SKTextAlign.Left, font, litPaint);
                }

                // A partly sung letter brightens smoothly instead of popping.
                if (whole < text.Length && fraction > 0.01f)
                {
                    var edge = textX + font.MeasureText(text[..whole]);
                    var letterWidth = font.MeasureText(text.Substring(whole, 1));
                    ctx.Canvas.Save();
                    ctx.Canvas.ClipRect(new SKRect(edge, baseline + metrics.Ascent,
                        edge + letterWidth * fraction, baseline + metrics.Descent));
                    litPaint.Color = WithAlpha(textColor, alpha);
                    ctx.Canvas.DrawText(text, textX, baseline, SKTextAlign.Left, font, litPaint);
                    ctx.Canvas.Restore();
                }
            }

            ctx.Canvas.Restore();
        }
    }

    /// <summary>How much of the current line has been sung, as a fraction of its characters.</summary>
    private static float SungFraction(in StyleContext ctx)
    {
        if (ctx.Words.Count == 0)
        {
            return 0f;
        }

        var total = 0f;
        var sung = 0f;
        foreach (var word in ctx.Words)
        {
            var length = Math.Max(1, word.Text.Length);
            total += length + (word.Glued ? 0 : 1);
            sung += (float)word.FillAt(ctx.PositionMs) * length;
        }

        return total > 0 ? Math.Clamp(sung / total, 0f, 1f) : 0f;
    }

    private static void DrawWordWithFill(SKCanvas canvas, SKTypeface typeface, string text, float size,
        float centerX, float centerY, SKColor color, SKColor glowColor, float alpha, float fill, bool glow)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var font = RenderCache.Font(typeface, size);
        var metrics = font.Metrics;
        var width = font.MeasureText(text);
        var x = centerX - width / 2f;
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2f;

        using var basePaint = new SKPaint { IsAntialias = true, Color = WithAlpha(color, alpha * 0.55f) };
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, basePaint);

        if (fill <= 0f)
        {
            return;
        }

        var pad = Math.Max(3f, size * 0.18f);
        var blur = Math.Max(4f, size * 0.2f);
        var clip = new SKRect(x - pad, baseline + metrics.Ascent - pad,
            x + width * fill + pad * (fill >= 1f ? 1f : 0.5f), baseline + metrics.Descent + pad);

        if (glow && fill < 1f)
        {
            var glowFont = RenderCache.Font(typeface, size, subpixelEdging: false, subpixel: false);
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = WithAlpha(glowColor, alpha * 0.62f),
                MaskFilter = RenderCache.Blur(blur),
            };
            var glowPad = blur * 2f + 2f;
            var glowClip = new SKRect(
                MathF.Floor(x - glowPad),
                MathF.Floor(baseline + metrics.Ascent - glowPad),
                MathF.Ceiling(x + width * fill + glowPad * 0.5f),
                MathF.Ceiling(baseline + metrics.Descent + glowPad));
            canvas.Save();
            canvas.ClipRect(glowClip);
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, glowFont, glowPaint);
            canvas.Restore();
        }

        using var fillPaint = new SKPaint { IsAntialias = true, Color = WithAlpha(color, alpha) };
        canvas.Save();
        canvas.ClipRect(clip);
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, fillPaint);
        canvas.Restore();
    }

    /// <summary>Crisp four-point star: cardinal spikes, diagonal waist.</summary>
    private static void DrawStar4(SKCanvas canvas, SKPoint center, float outer, float inner, SKPaint paint)
    {
        var points = new SKPoint[9];
        for (var i = 0; i < 8; i++)
        {
            var angle = -Math.PI / 2 + i * Math.PI / 4;
            var radius = i % 2 == 0 ? outer : inner;
            points[i] = new SKPoint(
                center.X + (float)(Math.Cos(angle) * radius),
                center.Y + (float)(Math.Sin(angle) * radius));
        }

        points[8] = points[0];
        canvas.DrawPoints(SKPointMode.Polygon, points, paint);
    }

    /// <summary>Points along a quadratic curve, so curves need no path objects.</summary>
    private static SKPoint[] SampleQuadratic(SKPoint start, SKPoint control, SKPoint end, int samples = 16)
    {
        var points = new SKPoint[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            var t = (float)i / samples;
            var u = 1f - t;
            points[i] = new SKPoint(
                u * u * start.X + 2f * u * t * control.X + t * t * end.X,
                u * u * start.Y + 2f * u * t * control.Y + t * t * end.Y);
        }

        return points;
    }

    /// <summary>Room the enlarged active label needs beyond its normal slot.</summary>
    private static float ActiveExtra(float[] widths, int active) =>
        active >= 0 && active < widths.Length ? widths[active] * 0.4f : 0f;

    /// <summary>Word widths at one size, for laying labels out without collisions.</summary>
    private static float[] MeasureWords(SKTypeface typeface, IReadOnlyList<TimedWord> words, float size)
    {
        var font = RenderCache.Font(typeface, size, subpixelEdging: false);
        var widths = new float[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            widths[i] = font.MeasureText(words[i].Text);
        }

        return widths;
    }

    private static int ActiveIndex(IReadOnlyList<TimedWord> words, double positionMs)
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

    /// <summary>The first <paramref name="width"/> pixels of a line, with room for a blurred pass.</summary>
    private static void DrawTextUpTo(SKCanvas canvas, string text, float x, float baseline,
        SKFont font, SKPaint paint, float width, float pad)
    {
        var metrics = font.Metrics;
        canvas.Save();
        canvas.ClipRect(new SKRect(x - pad, baseline + metrics.Ascent - pad,
            x + width + pad, baseline + metrics.Descent + pad));
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
        canvas.Restore();
    }

    private static SKColor WithAlpha(SKColor color, float alpha) =>
        color.WithAlpha((byte)Math.Clamp(color.Alpha * alpha, 0, 255));
}
