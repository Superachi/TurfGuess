using SkiaSharp;
using TurfGuess.Core;
using TurfGuess.Core.Render;

namespace TurfGuess.Bot.Options;

/// <summary>All bot settings, bound from appsettings.json / environment / user secrets (DESIGN.md §7).</summary>
public sealed class BotOptions
{
    public GameOptions Game { get; set; } = new();
    public DiscordOptions Discord { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public ImageOptions Images { get; set; } = new();

    /// <summary>Optional. Leave empty to use the built-in color pairs.</summary>
    public List<ColorPair> ColorPairs { get; set; } = [];

    /// <summary>Map, paint and difficulty settings for the generator.</summary>
    public GeneratorOptions Generator { get; set; } = new();

    public IReadOnlyList<ColorPair> EffectiveColorPairs =>
        ColorPairs.Count > 0 ? ColorPairs : Core.Render.ColorPairs.Default;
}

public sealed class GameOptions
{
    public string Name { get; set; } = "Turf Guess";

    /// <summary>Wall-clock time of day (in <see cref="TimeZone"/>) when rounds are revealed and posted.</summary>
    public string RoundTime { get; set; } = "09:00";

    /// <summary>IANA time zone id, e.g. "Europe/Amsterdam".</summary>
    public string TimeZone { get; set; } = "Europe/Amsterdam";

    /// <summary>Show "N players have voted" on the round message.</summary>
    public bool ShowVoteCount { get; set; } = true;

    /// <summary>Most correct guessers listed by name; the rest are summarised as "…and N more".</summary>
    public int MaxListedWinners { get; set; } = 50;

    /// <summary>On first start, a round shorter than this skips to the following round time.</summary>
    public TimeSpan MinRoundLength { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How long a regular battle (started with /turf battle) stays open for votes.</summary>
    public TimeSpan BattleLength { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many minutes before the reveal a reminder to vote is posted under the ranked round. 0 turns it off.
    /// Rounds that open with less time left than this get no reminder.
    /// </summary>
    public int ReminderMinutes { get; set; } = 60;

    /// <summary>How the ink is drawn.</summary>
    public InkStyle Ink { get; set; } = InkStyle.Marbled;

    public TimeOnly ParsedRoundTime => TimeOnly.Parse(RoundTime, System.Globalization.CultureInfo.InvariantCulture);
    public TimeZoneInfo ParsedTimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}

public sealed class DiscordOptions
{
    /// <summary>The bot token. Never put it in appsettings.json: use TURFGUESS_Discord__Token or user secrets.</summary>
    public string Token { get; set; } = "";

    /// <summary>The channel the game is played in.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>How long to wait between vote-count edits on the round message.</summary>
    public TimeSpan VoteCountInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class DatabaseOptions
{
    public string Path { get; set; } = "turfguess.db";
}

public sealed class ImageOptions
{
    /// <summary>Where round and reveal PNGs are stored.</summary>
    public string Directory { get; set; } = "images";
}

/// <summary>Startup validation: the bot refuses to start with a broken configuration.</summary>
public static class BotOptionsValidator
{
    public static IReadOnlyList<string> Validate(BotOptions o)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(o.Discord.Token))
            errors.Add("Discord:Token is missing. Set the TURFGUESS_Discord__Token environment variable or use 'dotnet user-secrets'.");
        if (o.Discord.ChannelId == 0)
            errors.Add("Discord:ChannelId is missing.");
        if (o.Discord.VoteCountInterval < TimeSpan.FromSeconds(5))
            errors.Add("Discord:VoteCountInterval must be at least 5 seconds.");

        if (!TimeOnly.TryParse(o.Game.RoundTime, System.Globalization.CultureInfo.InvariantCulture, out _))
            errors.Add($"Game:RoundTime '{o.Game.RoundTime}' is not a time of day like \"09:00\".");
        try { _ = o.Game.ParsedTimeZone; }
        catch (Exception) { errors.Add($"Game:TimeZone '{o.Game.TimeZone}' is not a known time zone."); }
        if (o.Game.MaxListedWinners < 0) errors.Add("Game:MaxListedWinners must be >= 0.");
        if (o.Game.MinRoundLength < TimeSpan.Zero || o.Game.MinRoundLength >= TimeSpan.FromDays(1))
            errors.Add("Game:MinRoundLength must be between 0 and 24 hours.");

        if (o.Game.BattleLength < TimeSpan.FromMinutes(1) || o.Game.BattleLength > TimeSpan.FromHours(6))
            errors.Add("Game:BattleLength must be between 1 minute and 6 hours.");

        if (o.Game.ReminderMinutes < 0 || o.Game.ReminderMinutes > 1440)
            errors.Add("Game:ReminderMinutes must be between 0 (off) and 1440 (24 hours).");

        try { o.Generator.Validate(); }
        catch (ArgumentException ex) { errors.AddRange(ex.Message.Split(Environment.NewLine).Select(e => "Generator: " + e)); }

        for (int i = 0; i < o.ColorPairs.Count; i++)
        {
            var p = o.ColorPairs[i];
            string at = $"ColorPairs[{i}]";
            foreach (var (team, name) in new[] { (p.A, "A"), (p.B, "B") })
            {
                if (string.IsNullOrWhiteSpace(team.Name)) errors.Add($"{at}.{name}.Name is missing.");
                if (!SKColor.TryParse(team.Color, out _)) errors.Add($"{at}.{name}.Color '{team.Color}' is not a hex color.");
                if (string.IsNullOrWhiteSpace(team.Emoji)) errors.Add($"{at}.{name}.Emoji is missing.");
            }
            if (p.A.Name == p.B.Name) errors.Add($"{at}: both teams have the name '{p.A.Name}'.");
            if (p.A.Emoji == p.B.Emoji) errors.Add($"{at}: both teams have the emoji '{p.A.Emoji}'.");
        }

        return errors;
    }
}
