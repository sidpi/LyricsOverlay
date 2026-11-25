namespace LyricOverlay.Rendering;

/// <summary>Geometry shared by the live overlay and the headless renderer.</summary>
internal static class OverlayLayout
{
    /// <summary>Height of the lyric band the styles are laid out for.</summary>
    public const float StageHeight = 240f;

    /// <summary>Extra zoom applied to that band in full-screen mode, so the words read from across a room.</summary>
    public const float FullScreenZoom = 1.5f;
}
