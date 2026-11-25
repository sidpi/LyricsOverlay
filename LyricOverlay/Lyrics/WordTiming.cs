using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricOverlay.Lyrics;

/// <summary>A single word (or CJK character) with the window of time in which it is sung.</summary>
/// <param name="Glued">True when this token follows the previous one with no space between them
/// (CJK characters in a word), so layout must not insert a gap.</param>
public sealed record TimedWord(string Text, double StartMs, double EndMs, bool Glued = false)
{
    public double DurationMs => Math.Max(1, EndMs - StartMs);

    /// <summary>How much of this word has been sung at <paramref name="positionMs"/>, 0..1.</summary>
    public double FillAt(double positionMs) =>
        Math.Clamp((positionMs - StartMs) / DurationMs, 0, 1);
}

/// <summary>
/// Produces word timings for a lyric line. Uses inline enhanced-LRC stamps
/// (&lt;mm:ss.xx&gt;) when the lyric source provides them; otherwise distributes the line's
/// duration across its tokens.
///
/// CJK scripts are written without spaces, so a whitespace split would make an entire
/// Korean line a single "word" that fills all at once. CJK runs are therefore split per
/// character, while Latin words stay whole — mixed lines get both behaviours.
/// </summary>
public static class WordTiming
{
    private static readonly Regex InlineStamp = new(@"<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>", RegexOptions.Compiled);

    /// <summary>True when the text contains Han, Hangul or kana characters.</summary>
    public static bool ContainsCjk(string? text) => !string.IsNullOrEmpty(text) && text.Any(IsCjk);

    public static IReadOnlyList<TimedWord> Build(LyricLine line, TimeSpan lineEnd)
    {
        var text = line.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return Array.Empty<TimedWord>();
        }

        if (InlineStamp.IsMatch(text))
        {
            var stamped = FromInlineStamps(text, line.Time, lineEnd);
            if (stamped.Count > 0)
            {
                return stamped;
            }
        }

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
        {
            return Array.Empty<TimedWord>();
        }

        var start = line.Time.TotalMilliseconds;
        var end = lineEnd.TotalMilliseconds > start
            ? Math.Min(lineEnd.TotalMilliseconds, start + 15000)
            : start + 3000;
        end = Math.Max(end, start + 600);

        // Weight Latin words by length, CJK characters evenly.
                    var weights = tokens.Select(t => IsCjkToken(t.Text) ? 1.0 : Math.Max(1, t.Text.Length)).ToArray();
        var total = weights.Sum();

        // Lines are often followed by an instrumental gap or a held note. Stretching the words
        // across that whole gap makes the fill crawl behind the voice and snap forward at the
        // next line, so an obviously over-long span is trimmed to a plausible sung duration.
        var lineSpan = end - start;
        var estimate = tokens.Sum(t => IsCjkToken(t.Text) ? 230.0 : 120.0 + 55.0 * t.Text.Length);
        if (lineSpan > estimate * 2.2 && lineSpan > 3200)
        {
            end = start + Math.Max(estimate * 1.3, 900);
        }

        var result = new List<TimedWord>(tokens.Count);
        var cursor = start;
        for (var i = 0; i < tokens.Count; i++)
        {
            var span = (end - start) * weights[i] / total;
            result.Add(new TimedWord(tokens[i].Text, cursor, cursor + span, tokens[i].Glued));
            cursor += span;
        }

        return result;
    }

    /// <summary>Splits into Latin words and individual CJK characters, dropping whitespace.</summary>
    public static List<(string Text, bool Glued)> Tokenize(string text)
    {
        var tokens = new List<(string Text, bool Glued)>();
        var buffer = new StringBuilder();
        var pendingSpace = false;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            tokens.Add((buffer.ToString(), tokens.Count > 0 && !pendingSpace));
            buffer.Clear();
            pendingSpace = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // Rare ideographs (CJK extension B and beyond) are surrogate pairs: keep them whole
            // so one character stays one token instead of splitting into halves.
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                Flush();
                tokens.Add((text.Substring(i, 2), tokens.Count > 0 && !pendingSpace));
                pendingSpace = false;
                i++;
                continue;
            }

            if (IsCjk(ch))
            {
                Flush();
                tokens.Add((ch.ToString(), tokens.Count > 0 && !pendingSpace));
                pendingSpace = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                Flush();
                pendingSpace = true;
            }
            else
            {
                buffer.Append(ch);
            }
        }

        Flush();
        return tokens;
    }

    private static bool IsCjkToken(string token) => token.Length == 1 && IsCjk(token[0]);

    private static bool IsCjk(char ch) =>
        (ch >= '\u1100' && ch <= '\u11FF') ||   // Hangul Jamo
        (ch >= '\u3130' && ch <= '\u318F') ||   // Hangul compatibility Jamo
        (ch >= '\uAC00' && ch <= '\uD7AF') ||   // Hangul syllables
        (ch >= '\u3040' && ch <= '\u30FF') ||   // Hiragana / Katakana
        (ch >= '\u3400' && ch <= '\u4DBF') ||   // CJK extension A
        (ch >= '\u4E00' && ch <= '\u9FFF') ||   // CJK unified ideographs
        (ch >= '\uF900' && ch <= '\uFAFF') ||   // CJK compatibility ideographs
        (ch >= '\uFF66' && ch <= '\uFF9D') ||   // halfwidth katakana
        (ch >= '\uFFA0' && ch <= '\uFFDC');     // halfwidth Hangul

    private static List<TimedWord> FromInlineStamps(string text, TimeSpan lineStart, TimeSpan lineEnd)
    {
        var result = new List<TimedWord>();
        var matches = InlineStamp.Matches(text);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var wordStart = StampToMs(match);
            var contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var content = text[(match.Index + match.Length)..contentEnd].Trim();
            if (content.Length == 0)
            {
                continue;
            }

            var wordEnd = i + 1 < matches.Count ? StampToMs(matches[i + 1]) : lineEnd.TotalMilliseconds;
            // Stamped lyrics are Latin in practice; spacing stays word-separated.
            result.Add(new TimedWord(content, wordStart, Math.Max(wordStart + 80, wordEnd)));
        }

        var leading = text[..matches[0].Index].Trim();
        if (leading.Length > 0)
        {
            result.Insert(0, new TimedWord(leading, lineStart.TotalMilliseconds, StampToMs(matches[0])));
        }

        return result.Where(w => w.EndMs > w.StartMs).ToList();
    }

    private static double StampToMs(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var fractionMs = 0;
        if (match.Groups[3].Success)
        {
            var fraction = match.Groups[3].Value;
            fractionMs = int.Parse(fraction, CultureInfo.InvariantCulture) *
                         (fraction.Length == 1 ? 100 : fraction.Length == 2 ? 10 : 1);
        }

        return (minutes * 60 + seconds) * 1000.0 + fractionMs;
    }
}
