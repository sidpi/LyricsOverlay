using System;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LyricOverlay.Services;

/// <summary>A normalized reading of whatever app currently owns the Windows media session.</summary>
public sealed record PlaybackSnapshot(
    string Title,
    string Artist,
    string Album,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPlaying,
    string SourceApp)
{
    /// <summary>Identity of the track, used to cache lyric lookups.</summary>
    public string Key => $"{Artist}\u241F{Title}";
}

public static class MediaSessionService
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;

    /// <summary>Raw artwork bytes for the current track, or null when the session has none.</summary>
    public static async System.Threading.Tasks.Task<byte[]?> ReadArtworkAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager?.GetCurrentSession();
            if (session is null)
            {
                return null;
            }

            var media = await session.TryGetMediaPropertiesAsync();
            var thumbnail = media?.Thumbnail;
            if (thumbnail is null)
            {
                return null;
            }

            using var stream = await thumbnail.OpenReadAsync();
            var length = (int)stream.Size;
            if (length <= 0 || length > 12_000_000)
            {
                return null;
            }

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)length);
            var bytes = new byte[length];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the current system media session (Spotify, Apple Music, browsers, ...).
    /// Returns null when nothing is playing or the session is unavailable.
    /// </summary>
    public static async System.Threading.Tasks.Task<PlaybackSnapshot?> ReadAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager?.GetCurrentSession();
            if (session is null)
            {
                return null;
            }

            var media = await session.TryGetMediaPropertiesAsync();
            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var isPlaying = info?.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // SMTC exposes the position as of LastUpdatedTime, not "now": advance it while playing
            // so the lyric line tracks the audio instead of lagging behind by a poll interval.
            var position = timeline.Position;
            if (isPlaying)
            {
                // Some players refresh LastUpdatedTime rarely (or only on seek), so drift can
                // legitimately be many seconds old; dropping those left the anchor stale.
                var drift = DateTimeOffset.Now - timeline.LastUpdatedTime;
                if (drift > TimeSpan.Zero && drift < TimeSpan.FromSeconds(30))
                {
                    position += drift;
                }
            }

            var duration = timeline.EndTime > timeline.StartTime
                ? timeline.EndTime
                : timeline.MaxSeekTime > timeline.StartTime
                    ? timeline.MaxSeekTime
                    : TimeSpan.Zero;

            return new PlaybackSnapshot(
                media?.Title ?? string.Empty,
                media?.Artist ?? string.Empty,
                media?.AlbumTitle ?? string.Empty,
                position,
                duration,
                isPlaying,
                session.SourceAppUserModelId ?? string.Empty);
        }
        catch
        {
            // The session manager can throw while apps start/stop; treat it as "nothing playing".
            return null;
        }
    }
}
