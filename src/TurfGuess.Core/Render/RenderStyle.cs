namespace TurfGuess.Core.Render;

public sealed class TeamStyle
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>Shown on the Discord vote button.</summary>
    public string Emoji { get; set; } = "";
}

/// <summary>Visual settings for one round: which team is which color.</summary>
public sealed class RenderStyle
{
    public string GameName { get; set; } = "Turf Guess";
    public TeamStyle Left { get; set; } = new() { Name = "Orange", Color = "#CF581B", Emoji = "🧡" };
    public TeamStyle Right { get; set; } = new() { Name = "Blue", Color = "#141494", Emoji = "💙" };
}
