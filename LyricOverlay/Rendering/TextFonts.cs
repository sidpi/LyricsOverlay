using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace LyricOverlay.Rendering;

/// <summary>
/// Picks a typeface that can actually draw the line. A Latin mono font such as Cascadia Mono
/// covers no Hangul, kana or Han, so those lyrics would otherwise render as tofu boxes.
///
/// The decision is cached: coverage is fingerprinted per distinct set of non-Latin characters,
/// so a Japanese line is only scanned once instead of on every one of the 30 frames a second.
/// </summary>
internal static class TextFonts
{
    private const int CacheLimit = 512;

    private static readonly ConcurrentDictionary<(string Family, int Weight, int Slant, ulong Fingerprint), SKTypeface> Cache = new();

    // Ordered fallbacks per script; the first family that covers every character wins.
    private static readonly string[] Korean = { "Malgun Gothic", "Yu Gothic UI", "Microsoft JhengHei", "Noto Sans CJK KR" };
    private static readonly string[] Japanese = { "Yu Gothic UI", "Meiryo", "MS Gothic", "Noto Sans CJK JP" };
    private static readonly string[] Chinese = { "Microsoft YaHei", "Microsoft JhengHei", "SimSun", "Noto Sans CJK SC" };
    private static readonly string[] Generic = { "Segoe UI", "Arial Unicode MS", "Microsoft YaHei" };

    public static SKTypeface ForText(SKTypeface preferred, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return preferred;
        }

        var fingerprint = Fingerprint(text, out var mask);
        if (mask == 0)
        {
            return preferred; // ASCII/Latin: the user's chosen font is the right one.
        }

        var style = preferred.FontStyle;
        var key = (preferred.FamilyName ?? string.Empty, (int)style.Weight, (int)style.Slant, fingerprint);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = Resolve(preferred, text, mask);
        if (Cache.Count >= CacheLimit)
        {
            Cache.Clear();
        }

        Cache[key] = resolved;
        return resolved;
    }

    private static SKTypeface Resolve(SKTypeface preferred, string text, int mask)
    {
        if (Covers(preferred, text))
        {
            return preferred;
        }

        foreach (var family in Candidates(mask))
        {
            var face = SKTypeface.FromFamilyName(family, preferred.FontStyle);
            if (face is not null && Covers(face, text))
            {
                return face;
            }
        }

        // Last resort: ask the platform font manager per unsupported character.
        foreach (var ch in text.Where(c => c > 0x7F && !char.IsWhiteSpace(c)))
        {
            using var probe = new SKFont(preferred, 24);
            if (probe.GetGlyph(ch) != 0)
            {
                continue;
            }

            if (SKFontManager.Default.MatchCharacter(preferred.FamilyName, ch) is { } match)
            {
                return match;
            }
        }

        return preferred;
    }

    private static IEnumerable<string> Candidates(int mask)
    {
        if ((mask & Scripts.Hangul) != 0)
        {
            foreach (var family in Korean)
            {
                yield return family;
            }
        }

        if ((mask & Scripts.Kana) != 0)
        {
            foreach (var family in Japanese)
            {
                yield return family;
            }
        }

        if ((mask & Scripts.Han) != 0)
        {
            foreach (var family in Chinese)
            {
                yield return family;
            }
        }

        foreach (var family in Generic)
        {
            yield return family;
        }
    }

    /// <summary>True when every visible character maps to a real glyph in <paramref name="face"/>.</summary>
    private static bool Covers(SKTypeface face, string text)
    {
        using var font = new SKFont(face, 24);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsSurrogate(ch))
            {
                continue;
            }

            if (font.GetGlyph(ch) == 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Script bits present in the text, plus a stable fingerprint of its non-Latin characters.</summary>
    private static ulong Fingerprint(string text, out int mask)
    {
        mask = 0;
        ulong hash = 1469598103934665603UL;
        var seen = new SortedSet<char>();

        foreach (var ch in text)
        {
            if (ch <= 0x7F)
            {
                continue;
            }

            if (IsHangul(ch))
            {
                mask |= Scripts.Hangul;
            }
            else if (IsKana(ch))
            {
                mask |= Scripts.Kana;
            }
            else if (IsHan(ch))
            {
                mask |= Scripts.Han;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                mask |= Scripts.Other;
            }

            if (!seen.Add(ch))
            {
                continue;
            }

            hash = (hash ^ ch) * 1099511628211UL;
        }

        return hash;
    }

    private static bool IsHangul(char ch) => (ch >= '\u1100' && ch <= '\u11FF') ||
                                             (ch >= '\u3130' && ch <= '\u318F') ||
                                             (ch >= '\uAC00' && ch <= '\uD7AF');

    private static bool IsKana(char ch) => (ch >= '\u3040' && ch <= '\u309F') || (ch >= '\u30A0' && ch <= '\u30FF');

    private static bool IsHan(char ch) => (ch >= '\u4E00' && ch <= '\u9FFF') ||
                                          (ch >= '\u3400' && ch <= '\u4DBF') ||
                                          (ch >= '\uF900' && ch <= '\uFAFF');

    private static class Scripts
    {
        public const int Hangul = 1;
        public const int Kana = 2;
        public const int Han = 4;
        public const int Other = 8;
    }
}
