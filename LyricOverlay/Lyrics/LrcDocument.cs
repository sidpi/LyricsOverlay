using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LyricOverlay.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Parsed .lrc timing document: one entry per timestamped line.</summary>
public sealed class LrcDocument
{
    private static readonly Regex StampRegex = new(
        @"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    /// <summary>A <c>[offset:±ms]</c> tag shifts every timestamp by that many milliseconds.</summary>
    private static readonly Regex OffsetRegex = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private LrcDocument(IReadOnlyList<LyricLine> lines) => Lines = lines;

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>Builds a document from already-timed lines (e.g. a word-timed source).</summary>
    public static LrcDocument? FromLines(IEnumerable<LyricLine> lines)
    {
        var ordered = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => line.Time)
            .ToList();

        return ordered.Count == 0 ? null : new LrcDocument(ordered);
    }

    public static LrcDocument? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return null;
        }

        var lines = new List<LyricLine>();

        // LRC offset: many community files carry one, and ignoring it shifts the whole
        // track by a constant amount.
        var offset = TimeSpan.Zero;
        var offsetMatch = OffsetRegex.Match(lrc);
        if (offsetMatch.Success &&
            int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offsetMs))
        {
            offset = TimeSpan.FromMilliseconds(offsetMs);
        }

        foreach (var raw in lrc.Replace("\r\n", "\n").Split('\n'))
        {
            var stamps = StampRegex.Matches(raw);
            if (stamps.Count == 0)
            {
                continue; // metadata tags ([ar:], [ti:]) or untimed text
            }

            var text = StampRegex.Replace(raw, string.Empty).Trim();
            foreach (Match stamp in stamps)
            {
                var minutes = int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);

                var fractionMs = 0;
                if (stamp.Groups[3].Success)
                {
                    var fraction = stamp.Groups[3].Value;
                    // ".5" => 500ms, ".50" => 500ms, ".500" => 500ms
                    fractionMs = int.Parse(fraction, CultureInfo.InvariantCulture) *
                                 (fraction.Length == 1 ? 100 : fraction.Length == 2 ? 10 : 1);
                }

                var stampTime = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(fractionMs) + offset;
                if (stampTime < TimeSpan.Zero)
                {
                    stampTime = TimeSpan.Zero;
                }

                lines.Add(new LyricLine(stampTime, text));
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        return new LrcDocument(lines.OrderBy(l => l.Time).ToList());
    }

    /// <summary>The last non-empty line at or before <paramref name="position"/>.</summary>
    public LyricLine? LineAt(TimeSpan position)
    {
        LyricLine? found = null;
        foreach (var line in Lines)
        {
            if (line.Time > position)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line.Text))
            {
                found = line;
            }
        }

        return found;
    }

    /// <summary>The first non-empty line after <paramref name="position"/>.</summary>
    public LyricLine? NextAfter(TimeSpan position)
    {
        foreach (var line in Lines)
        {
            if (line.Time > position && !string.IsNullOrWhiteSpace(line.Text))
            {
                return line;
            }
        }

        return null;
    }
}
