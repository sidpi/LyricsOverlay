using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LyricOverlay.Lyrics;

/// <summary>
/// Parses the word-timed "lyricsfile" document some lyric records carry:
/// <code>
/// lines:
/// - text: When you were here before
///   start_ms: 19980
///   end_ms: 22048
///   words:
///   - text: 'When '
///     start_ms: 19980
/// </code>
/// Only this documented shape is read — it is line-oriented, so no YAML library is needed,
/// and anything unrecognised is skipped rather than guessed at.
/// </summary>
public static class LyricsFileParser
{
    public static (LrcDocument Document, IReadOnlyList<IReadOnlyList<TimedWord>> Words)? Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        var raw = new List<RawLine>();
        RawLine? current = null;
        // Line entries may be indented (lrc.red uses two spaces); word entries sit deeper still,
        // so the entry indent is what separates a new line from another word of the same line.
        var lineIndent = int.MaxValue;
        var inWords = false;

        foreach (var sourceLine in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = sourceLine.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (trimmed == "lines:" || trimmed.StartsWith("version:", StringComparison.Ordinal) ||
                trimmed.StartsWith("metadata:", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed == "words:")
            {
                inWords = true;
                continue;
            }

            if (trimmed.StartsWith("- text:", StringComparison.Ordinal))
            {
                var text = Unquote(trimmed["- text:".Length..]);

                if (current is not null && inWords && indent > lineIndent)
                {
                    current.Words.Add(new TimedWord(text, 0, 0));
                    continue;
                }

                current = new RawLine { Text = text };
                raw.Add(current);
                inWords = false;
                lineIndent = indent;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (trimmed.StartsWith("start_ms:", StringComparison.Ordinal) && TryNumber(trimmed, "start_ms:", out var start))
            {
                if (inWords && current.Words.Count > 0)
                {
                    current.Words[^1] = current.Words[^1] with { StartMs = start };
                }
                else
                {
                    current.Start = start;
                }

                continue;
            }

            if (trimmed.StartsWith("end_ms:", StringComparison.Ordinal) && TryNumber(trimmed, "end_ms:", out var end))
            {
                if (inWords && current.Words.Count > 0)
                {
                    current.Words[^1] = current.Words[^1] with { EndMs = end };
                }
                else
                {
                    current.End = end;
                }
            }
        }

        var lines = new List<LyricLine>();
        var wordsPerLine = new List<IReadOnlyList<TimedWord>>();

        for (var i = 0; i < raw.Count; i++)
        {
            var entry = raw[i];
            var text = entry.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var lineEnd = entry.End > entry.Start
                ? entry.End
                : i + 1 < raw.Count && raw[i + 1].Start > entry.Start
                    ? raw[i + 1].Start
                    : entry.Start + 3000;

            // Fill in missing word ends from the following word, then from the line end.
            var words = new List<TimedWord>();
            for (var w = 0; w < entry.Words.Count; w++)
            {
                var word = entry.Words[w];
                if (word.Text.Trim().Length == 0)
                {
                    continue;
                }

                var wordEnd = word.EndMs > word.StartMs
                    ? word.EndMs
                    : w + 1 < entry.Words.Count && entry.Words[w + 1].StartMs > word.StartMs
                        ? entry.Words[w + 1].StartMs
                        : lineEnd;

                words.Add(new TimedWord(word.Text.Trim(), word.StartMs, Math.Max(word.StartMs + 60, wordEnd)));
            }

            lines.Add(new LyricLine(TimeSpan.FromMilliseconds(entry.Start), text));
            wordsPerLine.Add(words);
        }

        var document = LrcDocument.FromLines(lines);
        if (document is null)
        {
            return null;
        }

        return (document, wordsPerLine);
    }

    private static bool TryNumber(string trimmed, string key, out double value)
    {
        var text = trimmed[key.Length..].Trim().Trim('"', '\'');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Unquote(string value)
    {
        var text = value.Trim();

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
        {
            return text[1..^1].Replace("''", "'");
        }

        return text;
    }

    private sealed class RawLine
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<TimedWord> Words { get; } = new();
    }
}
