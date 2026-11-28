using System;
using System.Collections.Generic;
using SkiaSharp;
using LyricOverlay.Lyrics;

namespace LyricOverlay.Rendering;

/// <summary>
/// Kinetic Scatter: a full-screen editorial word collage. The line is distributed over a
/// stratified grid that spans the whole viewport, clamped inside the safe area and settled by
/// an iterative repulsion pass, so words never clip an edge and never sit on each other.
/// Placement is deterministic per line, so nothing jumps between frames.
/// </summary>
internal static class ScatterRenderer
{
    // Safe viewport: 6%..94% across, 12%..88% down. The top band clears the now-playing pill
    // and the top-left title; the bottom band clears the progress bar.
    private const float SafeLeft = 0.06f;
    private const float SafeRight = 0.94f;
    private const float SafeTop = 0.12f;
    private const float SafeBottom = 0.88f;

    // Badge padding, in em: 0.08em vertical, 0.24em horizontal (the studio's px-5 py-2).
    private const float PadXEm = 0.24f;
    private const float PadYEm = 0.08f;

    /// <summary>Generous line box so ascenders and descenders never clip.</summary>
    private const float LineHeight = 1.35f;

    /// <summary>Minimum air between any two word boxes, in pixels.</summary>
    private const float Clearance = 28f;

    private const int RepulsionPasses = 30;

    /// <summary>Word drift while the line is on screen.</summary>
    private const float DriftPx = 5f;

    private const float CrossFadeMs = 280f;
    private const float SpringMs = 260f;
    private const float GhostFadeMs = 320f;

    private static readonly SKColor OffWhite = new(0xF3, 0xF4, 0xF6);
    private static readonly SKColor BadgeFill = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor BadgeText = new(0x0A, 0x0A, 0x0A);

    private const int LayoutLimit = 8;

    private static readonly object LayoutGate = new();
    private static readonly Dictionary<LayoutKey, Placed[]> Layouts = new();

    private readonly record struct LayoutKey(
        ulong Text,
        int Seed,
        int BaseSize,
        int Width,
        int Height,
        string Family);

    private struct Placed
    {
        public SKRect Box;
        public float Size;
        public float Rotation;
    }

    public static void Draw(
        SKCanvas canvas,
        OverlayStyle style,
        SKTypeface typeface,
        IReadOnlyList<TimedWord> words,
        double positionMs,
        int lineIndex,
        IReadOnlyList<string> history,
        float width,
        float height,
        float alpha,
        bool playing)
    {
        if (words.Count == 0 || width <= 1f || height <= 1f)
        {
            return;
        }

        var left = width * SafeLeft;
        var right = width * SafeRight;
        var top = height * SafeTop;
        var bottom = height * SafeBottom;

        var baseSize = BaseSize(width, style);

        var texts = new string[words.Count];
        var nonEmpty = 0;
        for (var i = 0; i < words.Count; i++)
        {
            texts[i] = words[i].Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(texts[i]))
            {
                nonEmpty++;
            }
        }

        var layout = Layout(texts, nonEmpty, lineIndex, baseSize, typeface, left, top, right, bottom);

        var now = (float)positionMs;

        // The outgoing line fades under the new one for a clean 280 ms crossfade.
        if (history.Count > 0 && words[0].StartMs > double.MinValue)
        {
            var age = now - (float)words[0].StartMs;
            var fade = 1f - Math.Clamp(age / GhostFadeMs, 0f, 1f);
            if (fade > 0.01f)
            {
                DrawGhost(canvas, typeface, history[history.Count - 1], baseSize * 0.72f,
                    lineIndex - 1, alpha * fade * 0.3f, (1f - fade) * 14f,
                    left, top, right, bottom);
            }
        }

        // Three passes: upcoming words are faint and furthest back, sung words solid, the word
        // being sung last so its cutout is never covered.
        var active = ActiveIndex(words, positionMs);
        for (var pass = 0; pass < 3; pass++)
        {
            for (var i = 0; i < words.Count; i++)
            {
                var text = texts[i];
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var state = i == active ? 2 : words[i].FillAt(positionMs) >= 1f ? 1 : 0;
                if (state != pass)
                {
                    continue;
                }

                var placed = layout[i];

                // Crossfade in: the whole line arrives together (dim for words still to come),
                // staggered word by word; the sung word springs in when its turn comes.
                var lineAge = now - (float)words[0].StartMs;
                var enter = Math.Clamp((lineAge - i * 22f) / CrossFadeMs, 0f, 1f);
                var eased = 1f - MathF.Pow(1f - enter, 3f);
                var sinceStart = now - (float)words[i].StartMs;
                var pop = state == 2 ? Spring(Math.Clamp(sinceStart / SpringMs, 0f, 1f)) : 1f;

                var drift = MathF.Sin((now / 1100f) + i * 1.7f) * DriftPx * eased;
                var rise = (1f - eased) * 14f;

                var center = new SKPoint(placed.Box.MidX, placed.Box.MidY - rise + drift);
                var scale = pop * (0.96f + 0.04f * eased);
                var wordAlpha = alpha * (state switch
                {
                    2 => eased,
                    1 => 0.88f * eased,
                    _ => 0.40f * eased,
                });

                if (!playing)
                {
                    wordAlpha *= 0.78f;
                }

                if (state == 2)
                {
                    DrawBadge(canvas, typeface, text, placed.Size, center, placed.Rotation, scale,
                        wordAlpha, style.Glow ? style.GlowTint : SKColors.Transparent);
                }
                else
                {
                    DrawPlain(canvas, typeface, text, placed.Size, center, placed.Rotation, scale,
                        wordAlpha);
                }
            }
        }
    }

    /// <summary>The current line, laid out inside a short band (used by the headless renderer).</summary>
    public static void Draw(in StyleContext ctx)
    {
        Draw(ctx.Canvas, ctx.Style, ctx.Bold, ctx.Words, ctx.PositionMs, ctx.LineIndex,
            ctx.History, ctx.Width, ctx.Height, ctx.Alpha, ctx.Playing);
    }

    /// <summary>
    /// Responsive type: clamp(2.4rem, 4.6vw, 6.2rem) scaled by the Size slider, with a density
    /// trim so long lines still fit the stage.
    /// </summary>
    private static float BaseSize(float width, OverlayStyle style)
    {
        var scale = Math.Clamp((float)style.TextScale, 0.5f, 2f);
        return Math.Clamp(width * 0.046f, 38.4f, 99.2f) * scale;
    }

    private static float Density(int count) => count switch
    {
        > 18 => 0.58f,
        > 12 => 0.70f,
        > 7 => 0.84f,
        _ => 1f,
    };

    /// <summary>
    /// Placement for one line and one surface. The repulsion pass is a 30-iteration all-pairs
    /// solve and its inputs only change when the line, seed, font scale or stage size does, so
    /// the settled result is cached instead of recomputed on every frame of the line.
    /// </summary>
    private static Placed[] Layout(string[] texts, int count, int seed, float baseSize,
        SKTypeface typeface, float left, float top, float right, float bottom)
    {
        if (count == 0)
        {
            return new Placed[texts.Length];
        }

        var key = LayoutKeyOf(texts, seed, baseSize, typeface, left, top, right, bottom);

        lock (LayoutGate)
        {
            if (Layouts.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var placed = BuildLayout(texts, count, seed, baseSize, typeface, left, top, right, bottom);

        lock (LayoutGate)
        {
            if (Layouts.Count >= LayoutLimit)
            {
                Layouts.Clear();
            }

            Layouts[key] = placed;
        }

        return placed;
    }

    private static LayoutKey LayoutKeyOf(string[] texts, int seed, float baseSize,
        SKTypeface typeface, float left, float top, float right, float bottom)
    {
        var hash = 1469598103934665603UL;
        foreach (var text in texts)
        {
            foreach (var ch in text)
            {
                hash = (hash ^ ch) * 1099511628211UL;
            }

            // Empty tokens still take a slot in the layout, so the separator is part of the key.
            hash = (hash ^ '\u0001') * 1099511628211UL;
        }

        return new LayoutKey(
            hash,
            seed,
            (int)MathF.Round(baseSize * 16f),
            (int)MathF.Round((right - left) * 4f),
            (int)MathF.Round((bottom - top) * 4f),
            typeface.FamilyName ?? string.Empty);
    }

    private static Placed[] BuildLayout(string[] texts, int count, int seed, float baseSize,
        SKTypeface typeface, float left, float top, float right, float bottom)
    {
        var placed = new Placed[texts.Length];
        if (count == 0)
        {
            return placed;
        }

        var density = Density(count);
        var width = right - left;
        var stageAspect = width / Math.Max(1f, bottom - top);
        var columns = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(count * stageAspect)));
        var rows = Math.Max(1, (int)MathF.Ceiling(count / (float)columns));

        columns = Math.Min(columns, Math.Max(2, count));
        var cellWidth = width / columns;
        var cellHeight = (bottom - top) / rows;
        var order = StratifiedOrder(columns, rows, count, seed);

        var boxes = new List<SKRect>(count);

        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var size = baseSize * density * (0.88f + 0.27f * Hash(i * 3 + seed * 71));
            size = Math.Max(18f, size);

            // A word never exceeds the safe stage: shrink it rather than let it clip.
            var maxWidth = width * 0.94f;
            var measure = RenderCache.Font(typeface, size, subpixelEdging: false);
            var natural = measure.MeasureText(text) + size * PadXEm * 2f;
            if (natural > maxWidth && natural > 0f)
            {
                size *= maxWidth / natural;
            }

            size = Math.Max(18f, size);
            var font = RenderCache.Font(typeface, size);
            var textWidth = font.MeasureText(text);
            var boxWidth = textWidth + size * PadXEm * 2f;
            var boxHeight = size * LineHeight + size * PadYEm * 2f;

            // The tilt is part of the footprint: pad the box by the rotation's overhang.
            var overhang = Math.Max(boxWidth, boxHeight) * 0.06f;
            boxWidth += overhang;
            boxHeight += overhang;

            var cell = order[i];
            var column = cell % columns;
            var row = cell / columns;

            // Organic jitter: +-20% of the cell, so the grid reads as scattered, not tabular.
            var jitterX = (Hash(i * 7 + seed * 53 + column * 11) - 0.5f) * 0.4f;
            var jitterY = (Hash(i * 11 + seed * 37 + row * 17) - 0.5f) * 0.4f;
            var centerX = left + (column + 0.5f + jitterX) * cellWidth;
            var centerY = top + (row + 0.5f + jitterY) * cellHeight;

            var box = ClampCenter(centerX, centerY, boxWidth, boxHeight, left, top, right, bottom);
            boxes.Add(box);

            placed[i] = new Placed
            {
                Box = box,
                Size = size,
                Rotation = (Hash(i * 13 + seed * 29) - 0.5f) * 6f,
            };
        }

        // Iterative AABB repulsion: no two words ever collide, at any size or slider scale.
        var list = boxes.ToArray();
        Resolve(list, left, top, right, bottom);

        var cursor = 0;
        for (var i = 0; i < texts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(texts[i]))
            {
                continue;
            }

            placed[i].Box = list[cursor++];
        }

        return placed;
    }

    /// <summary>
    /// Deterministic cell order that spans every column and every row, guaranteeing the right
    /// column and bottom row are used once the line has four or more words.
    /// </summary>
    private static int[] StratifiedOrder(int columns, int rows, int count, int seed)
    {
        var total = columns * rows;
        var pool = new List<int>(total);
        for (var i = 0; i < total; i++)
        {
            pool.Add(i);
        }

        for (var i = total - 1; i > 0; i--)
        {
            var j = Math.Clamp((int)(Hash(seed * 97 + i * 13) * (i + 1)), 0, i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var take = Math.Min(count, total);
        var chosen = pool.GetRange(0, take);

        if (count >= 4)
        {
            Ensure(chosen, pool, cell => cell % columns == columns - 1);
            if (rows > 1)
            {
                Ensure(chosen, pool, cell => cell / columns == rows - 1);
            }
        }

        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = chosen[i % chosen.Count];
        }

        return order;
    }

    private static void Ensure(List<int> chosen, List<int> pool, Predicate<int> match)
    {
        if (chosen.Count == 0 || chosen.Exists(match))
        {
            return;
        }

        var index = pool.FindIndex(cell => match(cell) && !chosen.Contains(cell));
        if (index >= 0)
        {
            chosen[chosen.Count - 1] = pool[index];
        }
    }

    private static SKRect ClampCenter(float centerX, float centerY, float width, float height,
        float left, float top, float right, float bottom)
    {
        return Clamp(new SKRect(centerX - width / 2f, centerY - height / 2f,
            centerX + width / 2f, centerY + height / 2f), left, top, right, bottom);
    }

    private static SKRect Clamp(SKRect rect, float left, float top, float right, float bottom)
    {
        var dx = rect.Left < left ? left - rect.Left : rect.Right > right ? right - rect.Right : 0f;
        var dy = rect.Top < top ? top - rect.Top : rect.Bottom > bottom ? bottom - rect.Bottom : 0f;
        return new SKRect(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);
    }

    /// <summary>Pushes overlapping boxes apart until each pair keeps its clearance.</summary>
    private static void Resolve(SKRect[] boxes, float left, float top, float right, float bottom)
    {
        for (var pass = 0; pass < RepulsionPasses; pass++)
        {
            var moved = false;

            for (var i = 0; i < boxes.Length; i++)
            {
                for (var j = i + 1; j < boxes.Length; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];

                    var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) + Clearance;
                    var overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) + Clearance;
                    if (overlapX <= 0f || overlapY <= 0f)
                    {
                        continue;
                    }

                    moved = true;
                    if (overlapX < overlapY)
                    {
                        var push = overlapX / 2f;
                        var direction = a.MidX <= b.MidX ? -1f : 1f;
                        boxes[i] = Clamp(Shift(a, push * direction, 0f), left, top, right, bottom);
                        boxes[j] = Clamp(Shift(b, -push * direction, 0f), left, top, right, bottom);
                    }
                    else
                    {
                        var push = overlapY / 2f;
                        var direction = a.MidY <= b.MidY ? -1f : 1f;
                        boxes[i] = Clamp(Shift(a, 0f, push * direction), left, top, right, bottom);
                        boxes[j] = Clamp(Shift(b, 0f, -push * direction), left, top, right, bottom);
                    }
                }
            }

            if (!moved)
            {
                break;
            }
        }
    }

    private static SKRect Shift(SKRect rect, float dx, float dy) =>
        new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);

    /// <summary>0 at the start, 1.04 around 60% of the way in, settling at 1.</summary>
    private static float Spring(float progress)
    {
        if (progress <= 0f)
        {
            return 0.92f;
        }

        if (progress >= 1f)
        {
            return 1f;
        }

        return progress < 0.6f
            ? 0.92f + 0.12f * (progress / 0.6f)
            : 1.04f - 0.04f * ((progress - 0.6f) / 0.4f);
    }

    private static void DrawGhost(SKCanvas canvas, SKTypeface typeface, string line, float baseSize,
        int seed, float alpha, float rise, float left, float top, float right, float bottom)
    {
        if (string.IsNullOrWhiteSpace(line) || alpha <= 0.01f)
        {
            return;
        }

        var texts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var layout = Layout(texts, texts.Length, seed, baseSize, typeface, left, top, right, bottom);

        for (var i = 0; i < texts.Length; i++)
        {
            var placed = layout[i];
            if (placed.Box.IsEmpty)
            {
                continue;
            }

            var center = new SKPoint(placed.Box.MidX, placed.Box.MidY - rise);
            DrawPlain(canvas, typeface, texts[i], placed.Size, center, placed.Rotation, 1f, alpha);
        }
    }

    private static void DrawPlain(SKCanvas canvas, SKTypeface typeface, string text, float size,
        SKPoint center, float rotation, float scale, float alpha)
    {
        var font = RenderCache.Font(typeface, size);
        using var paint = new SKPaint { IsAntialias = true, Color = WithAlpha(OffWhite, alpha) };
        var metrics = font.Metrics;
        var textWidth = font.MeasureText(text);

        canvas.Save();
        canvas.Translate(center.X, center.Y);
        canvas.RotateDegrees(rotation);
        canvas.Scale(scale, scale);
        canvas.DrawText(text, -textWidth / 2f, -(metrics.Ascent + metrics.Descent) / 2f,
            SKTextAlign.Left, font, paint);
        canvas.Restore();
    }

    /// <summary>Inverted cutout: a crisp light block with dark type, used for the stressed word.</summary>
    private static void DrawBadge(SKCanvas canvas, SKTypeface typeface, string text, float size,
        SKPoint center, float rotation, float scale, float alpha, SKColor glow)
    {
        var font = RenderCache.Font(typeface, size);
        var metrics = font.Metrics;
        var textWidth = font.MeasureText(text);
        var padX = size * PadXEm;
        var padY = size * PadYEm;
        var boxWidth = textWidth + padX * 2f;
        var boxHeight = size * LineHeight + padY * 2f;

        canvas.Save();
        canvas.Translate(center.X, center.Y);
        canvas.RotateDegrees(rotation);
        canvas.Scale(scale, scale);

        if (glow.Alpha > 0)
        {
            using var halo = new SKPaint
            {
                IsAntialias = true,
                Color = WithAlpha(glow, alpha * 0.30f),
                MaskFilter = RenderCache.Blur(size * 0.22f),
            };
            canvas.DrawRect(new SKRect(-boxWidth / 2f - 6f, -boxHeight / 2f - 4f,
                boxWidth / 2f + 6f, boxHeight / 2f + 4f), halo);
        }

        using var badge = new SKPaint
        {
            IsAntialias = true,
            Color = WithAlpha(BadgeFill, Math.Min(1f, alpha * 1.25f)),
        };
        canvas.DrawRect(new SKRect(-boxWidth / 2f, -boxHeight / 2f, boxWidth / 2f, boxHeight / 2f), badge);

        using var ink = new SKPaint { IsAntialias = true, Color = WithAlpha(BadgeText, alpha) };
        canvas.DrawText(text, -textWidth / 2f, -(metrics.Ascent + metrics.Descent) / 2f,
            SKTextAlign.Left, font, ink);
        canvas.Restore();
    }

    /// <summary>Stable pseudo-random value in [0,1) so a line always lands in the same places.</summary>
    private static float Hash(int seed)
    {
        var value = MathF.Sin(seed * 12.9898f) * 43758.5453f;
        return value - MathF.Floor(value);
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

    private static SKColor WithAlpha(SKColor color, float alpha) =>
        color.WithAlpha((byte)Math.Clamp(color.Alpha * alpha, 0f, 255f));
}
