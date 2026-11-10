using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Render;

namespace TurfGuess.Bot.Tests;

public class MessageTextTests
{
    private static RoundRecord Round() => new()
    {
        Id = 12, RevealAt = 1_800_000_000,
        LeftName = "Pink", LeftEmoji = "🩷", RightName = "Green", RightEmoji = "💚",
        Winner = "left", ShareLeft = 0.462, ShareRight = 0.396, Unpainted = 0.142,
    };

    [Fact]
    public void Reveal_names_the_winner_coverage_and_votes()
    {
        var text = MessageText.Reveal(Round(), new RevealSummary(3, 2, [1, 2, 3]), maxListed: 50);
        Assert.Contains("🩷 Pink wins!", text);
        Assert.Contains("46.2%", text);
        Assert.Contains("39.6%", text);
        Assert.Contains("14.2% unpainted", text);
        Assert.Contains("Votes: 🩷 3 · 💚 2", text);
        Assert.Contains("<@1>, <@2>, <@3>", text);
        Assert.DoesNotContain("more", text);
    }

    [Fact]
    public void Reveal_cuts_off_at_max_listed()
    {
        var ids = Enumerable.Range(1, 10).Select(i => (ulong)i).ToList();
        var text = MessageText.Reveal(Round(), new RevealSummary(10, 0, ids), maxListed: 4);
        Assert.Contains("<@4>", text);
        Assert.DoesNotContain("<@5>", text);
        Assert.Contains("…and 6 more", text);
    }

    [Fact]
    public void Reveal_stays_within_the_discord_limit()
    {
        var ids = Enumerable.Range(0, 500).Select(i => 1_234_567_890_123_456_000UL + (ulong)i).ToList();
        var text = MessageText.Reveal(Round(), new RevealSummary(500, 0, ids), maxListed: 500);
        Assert.True(text.Length <= MessageText.DiscordMessageLimit, $"{text.Length} chars");
        Assert.Contains("more", text);
    }

    [Fact]
    public void Reveal_handles_no_votes_and_no_correct_guesses()
    {
        Assert.Contains("Nobody voted", MessageText.Reveal(Round(), new RevealSummary(0, 0, []), 50));
        Assert.Contains("Nobody guessed right", MessageText.Reveal(Round(), new RevealSummary(0, 4, []), 50));
    }

    [Fact]
    public void Round_description_uses_discord_timestamps_and_vote_count()
    {
        var open = MessageText.RoundDescription(Round(), 3, closed: false);
        Assert.Contains("<t:1800000000:R>", open);
        Assert.Contains("3 players have voted.", open);
        Assert.DoesNotContain("voted", MessageText.RoundDescription(Round(), null, closed: false));
        Assert.Contains("closed", MessageText.RoundDescription(Round(), 3, closed: true));
    }

    [Theory]
    [InlineData(0.5050, "X-rank difficulty 🔥🔥🔥🔥🔥")]
    [InlineData(0.5090, "S-rank difficulty 🔥🔥🔥🔥")]
    [InlineData(0.5130, "A-rank difficulty 🔥🔥🔥")]
    [InlineData(0.5170, "B-rank difficulty 🔥🔥")]
    [InlineData(0.5210, "C-rank difficulty 🔥")]
    public void Round_description_shows_the_difficulty_rank_open_and_closed(double winnerShare, string expected)
    {
        var round = Round();
        round.WinnerShare = winnerShare;
        string line = expected + "\n";
        Assert.Contains(line, MessageText.RoundDescription(round, 3, closed: false));
        Assert.Contains(line, MessageText.RoundDescription(round, 3, closed: true) + "\n");
    }

    [Fact]
    public void Embeds_refer_to_their_own_attachment() =>
        Assert.Equal("attachment://round_2.png", MessageText.AttachmentUrl("round_2.png"));

    [Fact]
    public void Button_ids_round_trip()
    {
        string id = MessageText.ButtonId(12, Side.Right);
        Assert.Equal("vote:12:right", id);
        var parts = id.Split(':');
        Assert.True(VoteService.TryParseButton(parts[1], parts[2], out long roundId, out var side));
        Assert.Equal(12, roundId);
        Assert.Equal(Side.Right, side);
        Assert.False(VoteService.TryParseButton("x", "left", out _, out _));
        Assert.False(VoteService.TryParseButton("1", "middle", out _, out _));
    }
}

public class OptionsValidationTests
{
    private static BotOptions Valid() => new() { Discord = { Token = "t", ChannelId = 1 } };

    [Fact]
    public void Defaults_with_token_and_channel_are_valid() => Assert.Empty(BotOptionsValidator.Validate(Valid()));

    [Fact]
    public void Missing_token_and_channel_are_reported()
    {
        var errors = BotOptionsValidator.Validate(new BotOptions());
        Assert.Contains(errors, e => e.Contains("Token"));
        Assert.Contains(errors, e => e.Contains("ChannelId"));
    }

    [Fact]
    public void Bad_time_zone_and_round_time_are_reported()
    {
        var o = Valid();
        o.Game.TimeZone = "Mars/Olympus";
        o.Game.RoundTime = "nine";
        var errors = BotOptionsValidator.Validate(o);
        Assert.Contains(errors, e => e.Contains("TimeZone"));
        Assert.Contains(errors, e => e.Contains("RoundTime"));
    }

    [Fact]
    public void Generator_problems_are_reported()
    {
        var o = Valid();
        o.Generator.Difficulty.MinWinnerShare = 49;
        Assert.Contains(BotOptionsValidator.Validate(o), e => e.StartsWith("Generator:"));
    }

    [Fact]
    public void Bad_color_pairs_are_reported()
    {
        var o = Valid();
        o.ColorPairs.Add(new ColorPair
        {
            A = new TeamStyle { Name = "Red", Color = "nope", Emoji = "❤️" },
            B = new TeamStyle { Name = "Red", Color = "#00FF00", Emoji = "❤️" },
        });
        var errors = BotOptionsValidator.Validate(o);
        Assert.Contains(errors, e => e.Contains("not a hex color"));
        Assert.Contains(errors, e => e.Contains("same") || e.Contains("both teams have the name"));
        Assert.Contains(errors, e => e.Contains("both teams have the emoji"));
    }

    [Fact]
    public void Built_in_pairs_are_used_when_none_are_configured()
    {
        Assert.Same(ColorPairs.Default, Valid().EffectiveColorPairs);
    }
}
