using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Tests;

public class MessagePreviewTests
{
    private static BotOptions Options() => new() { Generator = { Paint = { PixelsPerCell = 4 } } };
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Preview_shows_every_message_for_a_sample_round()
    {
        string text = MessagePreview.Render(Options(), 42, Now, raw: false);

        Assert.Contains("Turf Guess · Round #12", text);
        Assert.Contains("Which team do you think painted more? Cast your vote!", text);
        Assert.Contains("You voted", text);
        Assert.Contains("**11 players have voted:** 🎲@Player1, @Player2, 🎲@Player3", text);
        Assert.Contains(MessageText.VotingClosed, text);
        Assert.Contains("(disabled)", text);
        Assert.Contains("wins!", text);
        Assert.Contains("Round #12 · Results", text);
        Assert.Contains("@Player1", text);           // mentions resolved
        Assert.DoesNotContain("<@", text);
        Assert.DoesNotContain("<t:", text);           // timestamps resolved
        Assert.Contains("in 15 hours", text);         // 17:00 UTC = 18:00 Amsterdam (UTC+1) -> 09:00 next day
    }

    [Fact]
    public void Raw_preview_keeps_the_discord_markup()
    {
        string text = MessagePreview.Render(Options(), 42, Now, raw: true);
        Assert.Contains("<@", text);
        Assert.Contains("<t:", text);
    }
}
