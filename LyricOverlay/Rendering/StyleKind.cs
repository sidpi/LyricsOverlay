using System;
using System.Collections.Generic;

namespace LyricOverlay.Rendering;

/// <summary>How the words arrive on screen. The timings underneath are identical for every style.</summary>
public enum StyleKind
{
    Kinetic,
    Fisheye,
    Codestyle,
    Branches,
    Constellation,
    Bubble,
    NativeDesign,
    KineticScatter,
}

public static class StyleCatalog
{
    public static readonly IReadOnlyList<(StyleKind Kind, string Name, string Description)> All = new[]
    {
        (StyleKind.Kinetic, "Kinetic", "Words fill one at a time, centred."),
        (StyleKind.Fisheye, "Fisheye", "A tight stack bulging through a lens."),
        (StyleKind.Codestyle, "Code Style", "Three lines of source; the newest lyric types out under a red cursor."),
        (StyleKind.Branches, "Vine", "A wavy vine with a glowing tip; tendrils grow out as the tip reaches each word."),
        (StyleKind.Constellation, "Constellation", "Words as stars in a loose sky, drawn in time; the sung star flares."),
        (StyleKind.Bubble, "Bubble", "Recent lines as chat bubbles."),
        (StyleKind.NativeDesign, "Native Design", "The song as a chat thread: charcoal and blue bubbles, the newest revealing as it is sung."),
        (StyleKind.KineticScatter, "Kinetic Scatter", "Full screen only: words scatter across the whole stage, each landing in its own zone in time."),
    };

    /// <summary>Styles that only make sense on a full-screen stage (they use the whole viewport).</summary>
    public static bool IsFullScreenOnly(StyleKind kind) => kind == StyleKind.KineticScatter;

    public static readonly IReadOnlyList<string> Fonts = new[]
    {
        "SF Pro Display", "Segoe UI", "Segoe UI Variable Display", "Bahnschrift",
        "Georgia", "Consolas", "Cascadia Mono",
    };

    public static StyleKind Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return StyleKind.Kinetic;
        }

        foreach (var (kind, name, _) in All)
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return StyleKind.Kinetic;
    }

    public static string NameOf(StyleKind kind)
    {
        foreach (var (candidate, name, _) in All)
        {
            if (candidate == kind)
            {
                return name;
            }
        }

        return "Kinetic";
    }

    public static StyleKind Next(StyleKind kind, bool fullScreen = true)
    {
        var index = 0;
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Kind == kind)
            {
                index = i;
            }
        }

        for (var step = 1; step <= All.Count; step++)
        {
            var candidate = All[(index + step) % All.Count].Kind;
            if (fullScreen || !IsFullScreenOnly(candidate))
            {
                return candidate;
            }
        }

        return kind;
    }
}
