using System;
using System.Collections.Generic;
using SkiaSharp;

namespace LyricOverlay.Rendering;

/// <summary>
/// Shares the Skia objects every lyric frame asks for. Fonts, blur masks and the feather
/// gradient used to be built from scratch inside the paint path, so a 30 fps overlay created
/// and disposed of them hundreds of times a second even though their inputs repeat from frame
/// to frame: same typeface, same fitted size, same blur radius, same highlight colour.
///
/// Everything here is immutable once created, and painting happens on one thread (the SKElement
/// paint for the live overlay, the render thread for <c>--render</c>), so entries are shared
/// rather than cloned. The caches are capped and cleared instead of growing without bound, which
/// keeps a slider drag from accumulating a font per size step forever.
/// </summary>
internal static class RenderCache
{
    private const int FontLimit = 192;
    private const int BlurLimit = 96;
    private const int ShaderLimit = 8;

    private static readonly object Gate = new();
    private static readonly Dictionary<FontKey, SKFont> Fonts = new();
    private static readonly Dictionary<int, SKMaskFilter> Blurs = new();
    private static readonly Dictionary<ShaderKey, SKShader> Shaders = new();

    private readonly record struct FontKey(
        string Family,
        int Weight,
        int Slant,
        int Width,
        int Size,
        bool Subpixel,
        bool SubpixelEdging);

    private readonly record struct ShaderKey(SKColor Color, int Feather);

    /// <summary>
    /// A font at (approximately) <paramref name="size"/>. Sizes are snapped to a quarter pixel,
    /// which is finer than a fitted lyric size ever needs to differ.
    /// </summary>
    /// <param name="subpixelEdging">
    /// Subpixel antialiasing for glyphs drawn straight onto the surface; grayscale for blurred
    /// passes, where subpixel-positioned glyphs plus a mask filter land a fraction off.
    /// </param>
    public static SKFont Font(SKTypeface typeface, float size, bool subpixelEdging = true, bool subpixel = true)
    {
        var quantized = Quantize(size);
        var style = typeface.FontStyle;
        var key = new FontKey(
            typeface.FamilyName ?? string.Empty,
            (int)style.Weight,
            (int)style.Slant,
            (int)style.Width,
            (int)MathF.Round(quantized * 64f),
            subpixel,
            subpixelEdging);

        lock (Gate)
        {
            if (Fonts.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var font = new SKFont(typeface, quantized)
        {
            Subpixel = subpixel,
            Edging = subpixelEdging ? SKFontEdging.SubpixelAntialias : SKFontEdging.Antialias,
        };

        lock (Gate)
        {
            if (Fonts.Count >= FontLimit)
            {
                // Drop the references instead of disposing: a font handed out earlier in this
                // same frame may still be drawing, and the GC disposes what really is unused.
                Fonts.Clear();
            }

            Fonts[key] = font;
        }

        return font;
    }

    /// <summary>A blur mask filter for <paramref name="sigma"/>, snapped to a quarter pixel.</summary>
    public static SKMaskFilter Blur(float sigma)
    {
        var quantized = MathF.Max(0.25f, MathF.Round(sigma * 4f) / 4f);
        var key = (int)MathF.Round(quantized * 16f);

        lock (Gate)
        {
            if (Blurs.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, quantized);

        lock (Gate)
        {
            if (Blurs.Count >= BlurLimit)
            {
                Blurs.Clear(); // same reasoning as fonts: never dispose what a live paint may hold
            }

            Blurs[key] = filter;
        }

        return filter;
    }

    /// <summary>
    /// A left-to-right fade of <paramref name="color"/> over <paramref name="feather"/> pixels,
    /// anchored at the local origin so a translated canvas can place it on any word edge.
    /// </summary>
    public static SKShader Feather(SKColor color, float feather)
    {
        var length = MathF.Max(1f, MathF.Round(feather * 2f) / 2f);
        var key = new ShaderKey(color, (int)MathF.Round(length * 4f));

        lock (Gate)
        {
            if (Shaders.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var shader = SKShader.CreateLinearGradient(
            new SKPoint(0f, 0f),
            new SKPoint(length, 0f),
            new[] { color, color.WithAlpha(0) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);

        lock (Gate)
        {
            if (Shaders.Count >= ShaderLimit)
            {
                Shaders.Clear();
            }

            Shaders[key] = shader;
        }

        return shader;
    }

    private static float Quantize(float size) => MathF.Max(1f, MathF.Round(MathF.Max(1f, size) * 4f) / 4f);
}
