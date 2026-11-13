namespace TurfGuess.Core.Render;

public sealed class TeamStyle
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>Shown on the Discord vote button.</summary>
    public string Emoji { get; set; } = "";
}

/// <summary>How the ink surface is drawn.</summary>
public enum InkStyle
{
    /// <summary>One solid color per team.</summary>
    Flat,

    /// <summary>Thick ink with smooth swells, bump lighting, gloss and hue-shifted shading.</summary>
    Shaded,

    /// <summary>Swirled, oil-slick bands of hue-shifted tones (domain-warped noise). Default.</summary>
    Marbled,
}

/// <summary>A decorative layer drawn over the ink (on top of whichever <see cref="InkStyle"/> is used).</summary>
public enum PatternStyle
{
    /// <summary>No overlay.</summary>
    None,

    /// <summary>Scattered glints of light, with the odd four-point sparkle.</summary>
    Glitter,

    /// <summary>A tiled image supplied by <see cref="RenderStyle.PatternTexture"/>, screened over the paint.</summary>
    Texture,
}

/// <summary>Visual settings for one round: which team is which color.</summary>
/// <summary>Optional outline around the arena, drawn in the unpainted floor color.</summary>
public enum ArenaOutline
{
    /// <summary>No outline.</summary>
    None,
    /// <summary>Drawn just outside the arena edge, on the background.</summary>
    Outside,
    /// <summary>Drawn just inside the arena edge, over the outermost pixels.</summary>
    Inside,
}

public sealed class RenderStyle
{
    public string GameName { get; set; } = "Turf Guess";
    public InkStyle Ink { get; set; } = InkStyle.Marbled;
    public PatternStyle Pattern { get; set; } = PatternStyle.None;

    /// <summary>Path to a tileable PNG for <see cref="PatternStyle.Texture"/>. Light/opaque areas brighten the paint, black/transparent areas leave it alone.</summary>
    public string? PatternTexture { get; set; }

    /// <summary>How many cells one tile of the texture covers.</summary>
    public double PatternTextureCells { get; set; } = 8;

    public ArenaOutline Outline { get; set; } = ArenaOutline.Outside;
    public TeamStyle Left { get; set; } = new() { Name = "Orange", Color = "#CF581B", Emoji = "🧡" };
    public TeamStyle Right { get; set; } = new() { Name = "Blue", Color = "#141494", Emoji = "💙" };
}
