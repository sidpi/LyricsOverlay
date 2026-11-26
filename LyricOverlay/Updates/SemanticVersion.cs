using System;
using System.Globalization;

namespace LyricOverlay.Updates;

/// <summary>Semantic version with the prerelease ordering rules GitHub tags use (v1.2.3-beta.1).</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private readonly string[] _prerelease;

    private SemanticVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        _prerelease = prerelease?.Split('.') ?? Array.Empty<string>();
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text[0] is 'v' or 'V')
        {
            text = text[1..];
        }

        // Build metadata never participates in precedence.
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string? prerelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2],
            string.IsNullOrEmpty(prerelease) ? null : prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        // A release outranks any prerelease of the same core version.
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;

        var length = Math.Max(_prerelease.Length, other._prerelease.Length);
        for (var i = 0; i < length; i++)
        {
            if (i >= _prerelease.Length) return -1;
            if (i >= other._prerelease.Length) return 1;

            var left = _prerelease[i];
            var right = other._prerelease[i];
            var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var numbers = leftNumber.CompareTo(rightNumber);
                if (numbers != 0) return numbers;
            }
            else if (leftNumeric)
            {
                return -1;
            }
            else if (rightNumeric)
            {
                return 1;
            }
            else
            {
                var identifiers = string.CompareOrdinal(left, right);
                if (identifiers != 0) return identifiers;
            }
        }

        return 0;
    }

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
