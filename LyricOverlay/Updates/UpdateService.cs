using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LyricOverlay.Diagnostics;

namespace LyricOverlay.Updates;

public interface IUpdateService
{
    /// <summary>The running app version, without a leading v.</summary>
    string CurrentVersion { get; }

    /// <summary>True for the preview/mock service: nothing is installed and the app keeps running.</summary>
    bool Simulated { get; }

    /// <summary>The newest release, or null when the running build is already current.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Streams the installer to %TEMP% and returns its path.</summary>
    Task<string> DownloadAsync(UpdateInfo info, IProgress<DownloadProgress> progress, CancellationToken cancellationToken);

    Task VerifyAsync(string path, UpdateInfo info, CancellationToken cancellationToken);

    /// <summary>Hands the installer to Windows; the installer stops this process itself.</summary>
    Task InstallAsync(string path, UpdateInfo info, CancellationToken cancellationToken);
}

/// <summary>
/// GitHub Releases updater: reads the latest release, downloads the matching
/// LyricsOverlay-Setup-*.exe to %TEMP%, verifies it, then starts the NSIS installer.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string ReleasesApi = "https://api.github.com/repos/sidpi/LyricsOverlay/releases/latest";
    public const string RepositoryUrl = "https://github.com/sidpi/LyricsOverlay";

    private static readonly Regex SetupAsset = new(
        @"^LyricsOverlay-Setup-[^/\\]+\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Sha256Token = new(
        @"\b[0-9a-fA-F]{64}\b",
        RegexOptions.CultureInvariant);

    /// <summary>Checks are short; downloads must never time out mid-stream.</summary>
    private static readonly HttpClient Api = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly HttpClient Files = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly TimeSpan SpeedWindow = TimeSpan.FromMilliseconds(400);

    private readonly string _userAgent;

    public UpdateService()
    {
        CurrentVersion = ReadCurrentVersion();
        _userAgent = $"LyricsOverlay-Updater/{CurrentVersion} (+{RepositoryUrl})";
    }

    public string CurrentVersion { get; }

    public bool Simulated => false;

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await Api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Describe(ex, cancellationToken);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateException(
                    UpdateErrorKind.NotFound,
                    "No release is published yet, so there is nothing to update to.");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new UpdateException(
                    UpdateErrorKind.Network,
                    "GitHub is rate-limiting update checks right now. Try again in a few minutes.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException(
                    UpdateErrorKind.Network,
                    $"GitHub returned {(int)response.StatusCode} ({response.ReasonPhrase}) for the latest release.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return ReadRelease(document.RootElement);
            }
            catch (JsonException ex)
            {
                throw new UpdateException(
                    UpdateErrorKind.NotFound,
                    "The latest release could not be read from GitHub.",
                    ex);
            }
        }
    }

    private UpdateInfo? ReadRelease(JsonElement release)
    {
        var tag = release.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!SemanticVersion.TryParse(tag, out var version) || version is null)
        {
            throw new UpdateException(
                UpdateErrorKind.NotFound,
                "The latest release does not have a readable version tag.");
        }

        if (SemanticVersion.TryParse(CurrentVersion, out var current) && current is not null &&
            version.CompareTo(current) <= 0)
        {
            Log.Info($"update check: v{version} is not newer than v{CurrentVersion}");
            return null;
        }

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new UpdateException(
                UpdateErrorKind.NotFound,
                "The latest release has no downloadable assets.");
        }

        JsonElement? installer = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (name is not null && SetupAsset.IsMatch(name))
            {
                installer = asset;
                break;
            }
        }

        if (installer is null)
        {
            throw new UpdateException(
                UpdateErrorKind.NotFound,
                "The latest release does not include a LyricsOverlay-Setup-*.exe installer for Windows.");
        }

        var installerName = installer.Value.GetProperty("name").GetString()!;
        var installerUri = ReadUri(installer.Value)
            ?? throw new UpdateException(UpdateErrorKind.NotFound, "The installer asset has no download URL.");

        Uri? checksums = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (name is null || ReadUri(asset) is not { } uri)
            {
                continue;
            }

            // Prefer a per-installer .sha256, then a combined checksums.txt.
            if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                if (checksums is null || name.Contains(Path.GetFileNameWithoutExtension(installerName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    checksums = uri;
                }
            }
            else if (checksums is null && name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase))
            {
                checksums = uri;
            }
        }

        var notes = release.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString()?.Trim() : null;
        DateTimeOffset? published = null;
        if (release.TryGetProperty("published_at", out var publishedElement) &&
            publishedElement.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            published = parsed;
        }

        var size = installer.Value.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
            ? bytes
            : 0;

        Log.Info($"update available: v{version} (installer {installerName}, {size / (1024 * 1024)} MB)");
        return new UpdateInfo(
            version.ToString(),
            CurrentVersion,
            string.IsNullOrWhiteSpace(notes) ? null : notes,
            published,
            installerUri,
            installerName,
            size,
            checksums);
    }

    private static Uri? ReadUri(JsonElement asset) =>
        asset.TryGetProperty("browser_download_url", out var url) &&
        Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
            ? uri
            : null;

    public async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LyricsOverlay", "updates");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, info.InstallerName);

        Delete(target);

        var complete = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, info.InstallerUri);
            request.Headers.UserAgent.ParseAdd(_userAgent);

            using var response = await Files
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException(
                    UpdateErrorKind.Network,
                    $"The installer download failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
            }

            var total = response.Content.Headers.ContentLength ?? info.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[81920];
            long downloaded = 0;
            long windowBytes = 0;
            var windowStart = Stopwatch.GetTimestamp();
            var speed = 0d;

            progress.Report(new DownloadProgress(0, total, 0));

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                windowBytes += read;

                var elapsed = Stopwatch.GetElapsedTime(windowStart);
                if (elapsed >= SpeedWindow)
                {
                    var instant = windowBytes / elapsed.TotalSeconds;
                    speed = speed > 0 ? speed * 0.65 + instant * 0.35 : instant;
                    windowBytes = 0;
                    windowStart = Stopwatch.GetTimestamp();
                }

                progress.Report(new DownloadProgress(downloaded, total, speed));
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (total > 0 && downloaded != total)
            {
                throw new UpdateException(
                    UpdateErrorKind.Network,
                    "The download ended before the whole installer arrived. Check your connection and retry.");
            }

            complete = true;
            Log.Info($"update downloaded: {target} ({downloaded / (1024 * 1024)} MB)");
            return target;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            throw Describe(ex, cancellationToken);
        }
        finally
        {
            if (!complete)
            {
                Delete(target);
            }
        }
    }

    public async Task VerifyAsync(string path, UpdateInfo info, CancellationToken cancellationToken)
    {
        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateErrorKind.Verification, "The downloaded installer could not be read.", ex);
        }

        if (length <= 0)
        {
            throw new UpdateException(UpdateErrorKind.Verification, "The downloaded installer is empty.");
        }

        if (info.ChecksumsUri is not null && await ReadExpectedChecksumAsync(info, cancellationToken)
                .ConfigureAwait(false) is { } expected)
        {
            var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(
                    UpdateErrorKind.Verification,
                    "The installer's SHA-256 does not match the checksum published with the release, so it was not installed.");
            }

            Log.Info($"update verified with SHA-256 ({actual[..16]}…)");
            return;
        }

        if (!IsPortableExecutable(path))
        {
            throw new UpdateException(
                UpdateErrorKind.Verification,
                "The downloaded file is not a valid Windows installer.");
        }

        Log.Info("update verified with the PE header check (no matching checksum was published)");
    }

    /// <summary>The published hash for this installer, or null when the release lists no entry for it.</summary>
    private async Task<string?> ReadExpectedChecksumAsync(UpdateInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var text = await Api.GetStringAsync(info.ChecksumsUri!, cancellationToken).ConfigureAwait(false);
            var fileName = info.InstallerName;
            string? single = null;
            var count = 0;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var match = Sha256Token.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                count++;
                single = match.Value;

                if (line.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }
            }

            // A .sha256 file usually holds just the hash of the asset it is named after.
            var checksumName = info.ChecksumsUri!.Segments.LastOrDefault() ?? string.Empty;
            if (count == 1 && checksumName.Contains(Path.GetFileNameWithoutExtension(fileName),
                    StringComparison.OrdinalIgnoreCase))
            {
                return single;
            }

            Log.Info("the release checksums list no entry for the installer; using the PE header check");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // An unreachable checksum file must not block an otherwise valid installer.
            Log.Error("checksum download failed; falling back to the PE header check", ex);
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1 << 20,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>Cheap structural check: MZ header, a PE offset, and the PE signature.</summary>
    public static bool IsPortableExecutable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 0x40)
            {
                return false;
            }

            using var reader = new BinaryReader(stream);
            if (reader.ReadByte() != 'M' || reader.ReadByte() != 'Z')
            {
                return false;
            }

            stream.Position = 0x3C;
            var offset = reader.ReadInt32();
            if (offset < 0x40 || offset + 4 > stream.Length)
            {
                return false;
            }

            stream.Position = offset;
            return reader.ReadByte() == 'P' && reader.ReadByte() == 'E' &&
                   reader.ReadByte() == 0 && reader.ReadByte() == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public Task InstallAsync(string path, UpdateInfo info, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(path)
        {
            // /S runs NSIS silently; the script kills this process and relaunches the app.
            Arguments = "/S",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Path.GetTempPath(),
        };

        try
        {
            Process.Start(start);
        }
        catch (Exception ex)
        {
            throw new UpdateException(
                UpdateErrorKind.Install,
                "Windows could not start the installer. Run the downloaded setup file manually.",
                ex);
        }

        Log.Info($"installer launched for v{info.Version}: {path}");
        return Task.CompletedTask;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless; the next download overwrites it.
            Log.Error($"could not delete {path}", ex);
        }
    }

    private static string ReadCurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var text = informational?.Split('+')[0].Trim().TrimStart('v', 'V');
        if (!string.IsNullOrEmpty(text) && SemanticVersion.TryParse(text, out _))
        {
            return text;
        }

        var version = assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private UpdateException Describe(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is UpdateException known)
        {
            return known;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new UpdateException(UpdateErrorKind.Network, "The update was cancelled.");
        }

        if (ex is HttpRequestException http)
        {
            var offline = http.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.ConnectionError;

            return offline
                ? new UpdateException(
                    UpdateErrorKind.Offline,
                    "You're offline. Reconnect and try again.",
                    ex)
                : new UpdateException(
                    UpdateErrorKind.Network,
                    "The connection to GitHub failed before the request finished. Check your network and retry.",
                    ex);
        }

        if (ex is TaskCanceledException)
        {
            return new UpdateException(
                UpdateErrorKind.Network,
                "GitHub took too long to respond. Check your connection and retry.",
                ex);
        }

        return new UpdateException(
            UpdateErrorKind.Unknown,
            string.IsNullOrWhiteSpace(ex.Message) ? "Something went wrong while updating." : ex.Message,
            ex);
    }
}
