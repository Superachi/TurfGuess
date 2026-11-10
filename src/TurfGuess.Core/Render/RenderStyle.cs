namespace TurfGuess.Core.Render;

public sealed class TeamStyle
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";
}

/// <summary>Visual settings for the renderer. Names and colors are configurable (DESIGN.md §7).</summary>
public sealed class RenderStyle
{
    public string GameName { get; set; } = "Turf Guess";
    public TeamStyle Left { get; set; } = new() { Name = "Orange", Color = "#F28C28" };
    public TeamStyle Right { get; set; } = new() { Name = "Blue", Color = "#2F6FDE" };
}
