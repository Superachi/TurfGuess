using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Options;
using TurfGuess.Core;
using TurfGuess.Core.Render;

namespace TurfGuess.Bot.Game;

/// <summary>Renders and stores the round and reveal PNGs for a round.</summary>
public sealed class RoundImages(BotOptions options, ILogger<RoundImages> logger)
{
    public string Directory => Path.GetFullPath(options.Images.Directory);

    public RenderStyle StyleFor(RoundRecord round) => new()
    {
        GameName = options.Game.Name,
        Ink = options.Game.Ink,
        Left = new TeamStyle { Name = round.LeftName, Color = round.LeftColor, Emoji = round.LeftEmoji },
        Right = new TeamStyle { Name = round.RightName, Color = round.RightColor, Emoji = round.RightEmoji },
    };

    /// <summary>Writes both images and returns their paths.</summary>
    public (string ImagePath, string RevealImagePath) Save(RoundRecord round, GeneratedRound generated)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var style = StyleFor(round);
        string caption = $"{options.Game.Name} · {(round.Kind == RoundKind.Regular ? "Regular Battle" : "Round")} #{round.DisplayNumber}";

        string imagePath = Path.Combine(Directory, $"round_{round.Id}.png");
        string revealPath = Path.Combine(Directory, $"reveal_{round.Id}.png");

        using (var image = MapRenderer.RenderRound(generated, style, caption))
            File.WriteAllBytes(imagePath, MapRenderer.EncodePng(image));
        using (var reveal = MapRenderer.RenderReveal(generated, style, caption))
            File.WriteAllBytes(revealPath, MapRenderer.EncodePng(reveal));

        return (imagePath, revealPath);
    }

    /// <summary>
    /// Makes sure both image files exist (e.g. after a crash between creating the round and
    /// saving its images), regenerating them from the stored seed if needed.
    /// </summary>
    public (string ImagePath, string RevealImagePath) Ensure(RoundRecord round)
    {
        if (round.ImagePath.Length > 0 && File.Exists(round.ImagePath) &&
            round.RevealImagePath.Length > 0 && File.Exists(round.RevealImagePath))
            return (round.ImagePath, round.RevealImagePath);

        if (round.GeneratorVersion != RoundGenerator.GeneratorVersion)
            logger.LogWarning("Round {Id} was generated with version {Old}; regenerating its images with version {New} may differ.",
                round.Id, round.GeneratorVersion, RoundGenerator.GeneratorVersion);

        var generated = RoundGenerator.Generate((ulong)round.Seed, options.Generator, regular: round.Kind == RoundKind.Regular);
        return Save(round, generated);
    }
}
