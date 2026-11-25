using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace LyricOverlay.Rendering;

/// <summary>
/// Visual settings for the overlay: font, text scale and the three lyric tones
/// (base text, active highlight, secondary lines), plus glow and placement.
/// </summary>
public sealed class OverlayStyle
{
    public string Font { get; set; } = "Segoe UI";
    public double TextScale { get; set; } = 1.0;

    /// <summary>Upcoming / unsung words.</summary>
    public string TextColor { get; set; } = "#A6FFFFFF";

    /// <summary>Words already sung, and the active word's karaoke fill.</summary>
    public string HighlightColor { get; set; } = "#FFFFFFFF";

    /// <summary>Next line, header and hint text.</summary>
    public string SecondaryColor { get; set; } = "#73FFFFFF";

    public bool Glow { get; set; } = true;
    public int BottomMargin { get; set; } = 96;

    /// <summary>Animation style name; see <see cref="StyleCatalog"/>.</summary>
    public string Style { get; set; } = "Kinetic";

    /// <summary>Keep the display awake while a track plays.</summary>
    public bool KeepAwake { get; set; } = true;

    /// <summary>Cover the work area with the overlay and centre the lyric band.</summary>
    public bool FullScreen { get; set; } = false;

    /// <summary>Take the highlight (and secondary) tones from the album cover.</summary>
    public bool MatchArtwork { get; set; } = true;

    /// <summary>Preferred lyrics provider: "lrc.red" (default) or "lrclib". The other is the fallback.</summary>
    public string LyricsProvider { get; set; } = "lrc.red";

    /// <summary>Runtime colour from the current artwork; not persisted, so manual colours survive.</summary>
    [JsonIgnore]
    public SKColor? ArtworkHighlight { get; set; }

    [JsonIgnore]
    public SKColor? ArtworkSecondary { get; set; }

    /// <summary>Deeper, saturated tone from the artwork, used for the glow halo.</summary>
    [JsonIgnore]
    public SKColor? ArtworkGlow { get; set; }

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "overlay.json");

    public static OverlayStyle Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = JsonSerializer.Deserialize<OverlayStyle>(File.ReadAllText(ConfigPath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // A malformed config must not stop the overlay; fall back to defaults.
        }

        var defaults = new OverlayStyle();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    [JsonIgnore] public SKColor Text => Parse(TextColor, new SKColor(255, 255, 255, 0xA6));
    [JsonIgnore] public SKColor Highlight => ArtworkHighlight ?? Parse(HighlightColor, SKColors.White);
    [JsonIgnore] public SKColor Secondary => ArtworkSecondary ?? Parse(SecondaryColor, new SKColor(255, 255, 255, 0x73));

    /// <summary>Colour the glow halo is drawn in: the saturated artwork tone, else the highlight.</summary>
    [JsonIgnore] public SKColor GlowTint => ArtworkGlow ?? Parse(HighlightColor, SKColors.White);

    private static SKColor Parse(string value, SKColor fallback) =>
        SKColor.TryParse(value, out var color) ? color : fallback;
}
