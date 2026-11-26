using System;

namespace LyricOverlay.Updates;

/// <summary>A GitHub release that is newer than the running build.</summary>
public sealed record UpdateInfo(
    string Version,
    string CurrentVersion,
    string? Notes,
    DateTimeOffset? PublishedAt,
    Uri InstallerUri,
    string InstallerName,
    long Size,
    Uri? ChecksumsUri);

public readonly record struct DownloadProgress(long Downloaded, long Total, double BytesPerSecond)
{
    /// <summary>Exact completion, or 0 when the server did not send a length.</summary>
    public double Percent => Total > 0 ? Math.Min(100d, Downloaded * 100d / Total) : 0d;
}

public enum UpdateErrorKind
{
    Offline,
    NotFound,
    Network,
    Verification,
    Install,
    Unknown,
}

/// <summary>A failure the update panel can show verbatim.</summary>
public sealed class UpdateException : Exception
{
    public UpdateErrorKind Kind { get; }

    public UpdateException(UpdateErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;
}
