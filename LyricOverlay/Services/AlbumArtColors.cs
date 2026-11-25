using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkiaSharp;
using LyricOverlay.Diagnostics;

namespace LyricOverlay.Services;

/// <summary>
/// Two tones derived from one album cover: a light one that reads as lyric text and a
/// saturated one that carries the colour through the glow.
/// </summary>
internal readonly record struct ArtworkPalette(SKColor Highlight, SKColor Glow);

/// <summary>
/// Pulls the current track's artwork from the media session and derives its dominant colour.
/// Results are cached per track.
/// </summary>
internal static class AlbumArtColors
{
    private static readonly Dictionary<string, ArtworkPalette?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static async Task<ArtworkPalette?> GetAsync(string trackKey)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(trackKey, out var cached))
            {
                return cached;
            }
        }

        var palette = await ExtractAsync();
        lock (Gate)
        {
            Cache[trackKey] = palette;
        }

        return palette;
    }

    private static async Task<ArtworkPalette?> ExtractAsync()
    {
        try
        {
            var bytes = await MediaSessionService.ReadArtworkAsync();
            if (bytes is null || bytes.Length == 0)
            {
                Log.Info("album art: session exposed no artwork");
                return null;
            }

            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                Log.Info($"album art: decode failed ({bytes.Length} bytes)");
                return null;
            }

            var palette = Dominant(bitmap);
            Log.Info(palette is null
                ? $"album art: no dominant hue in {bitmap.Width}x{bitmap.Height} artwork"
                : $"album art: highlight {palette.Value.Highlight}, glow {palette.Value.Glow} from {bitmap.Width}x{bitmap.Height}");
            return palette;
        }
        catch (Exception ex)
        {
            Log.Error("album art colour extraction failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Picks the most vivid hue family, weighted by saturation and brightness, then builds
    /// two tones from it: a lifted one for text, a saturated one for the glow.
    /// </summary>
    private static ArtworkPalette? Dominant(SKBitmap bitmap)
    {
        const int bins = 12;
        var weight = new double[bins];
        var sumR = new double[bins];
        var sumG = new double[bins];
        var sumB = new double[bins];

        var stepX = Math.Max(1, bitmap.Width / 72);
        var stepY = Math.Max(1, bitmap.Height / 72);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 128)
                {
                    continue;
                }

                var (hue, saturation, lightness) = ToHsl(pixel);
                if (lightness < 0.12 || lightness > 0.95)
                {
                    continue; // near-black or blown-out white
                }

                var w = saturation * saturation * (0.35 + lightness);
                if (w <= 0.01)
                {
                    continue; // greys carry no hue
                }

                var bin = Math.Clamp((int)(hue / (360.0 / bins)), 0, bins - 1);
                weight[bin] += w;
                sumR[bin] += pixel.Red * w;
                sumG[bin] += pixel.Green * w;
                sumB[bin] += pixel.Blue * w;
            }
        }

        var best = -1;
        for (var i = 0; i < bins; i++)
        {
            if (weight[i] > 0 && (best < 0 || weight[i] > weight[best]))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var r = (byte)Math.Clamp(sumR[best] / weight[best], 0, 255);
        var g = (byte)Math.Clamp(sumG[best] / weight[best], 0, 255);
        var b = (byte)Math.Clamp(sumB[best] / weight[best], 0, 255);

        var (h, s, l) = ToHsl(new SKColor(r, g, b));

        // Text tone: light enough to read, still clearly the cover's hue.
        var highlight = FromHsl(h, Math.Clamp(s, 0.45, 0.92), Math.Clamp(Math.Max(l, 0.74), 0.74, 0.92));

        // Glow tone: same hue, pushed deeper and more saturated so the halo shows colour
        // instead of washing out to near-white.
        var glow = FromHsl(h, Math.Clamp(Math.Max(s, 0.55), 0.55, 1.0), Math.Clamp(l, 0.46, 0.60));

        return new ArtworkPalette(highlight, glow);
    }

    /// <summary>RGB to HSL with h in degrees and s/l in 0..1, computed explicitly.</summary>
    private static (double H, double S, double L) ToHsl(SKColor color)
    {
        var r = color.Red / 255.0;
        var g = color.Green / 255.0;
        var b = color.Blue / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;
        var delta = max - min;

        if (delta <= 0.0001)
        {
            return (0, 0, l);
        }

        var s = delta / (1 - Math.Abs(2 * l - 1));
        double h;

        if (max == r)
        {
            h = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            h = 60 * ((b - r) / delta + 2);
        }
        else
        {
            h = 60 * ((r - g) / delta + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        return (h, s, l);
    }

    private static SKColor FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        var m = l - c / 2;

        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new SKColor(
            (byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255));
    }
}
