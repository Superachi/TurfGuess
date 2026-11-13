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

    private static PlayerOutcome Out(ulong id, int delta, bool bet = false) => new(id, bet, delta);
    private static IReadOnlyList<Voter> V(params ulong[] ids) => ids.Select(i => new Voter(i, false)).ToList();
    private static RevealSummary Summary(int left, int right, IReadOnlyList<PlayerOutcome> winners,
        IReadOnlyList<PlayerOutcome>? losers = null, IReadOnlyList<StandingEntry>? standings = null) =>
        new(left, right, winners, losers ?? [], standings ?? []);

    [Fact]
    public void Reveal_names_the_winner_coverage_and_votes()
    {
        var text = MessageText.Reveal(Round(), Summary(3, 2, [Out(1, 6, bet: true), Out(2, 3), Out(3, 3)]), maxListed: 50);
        Assert.Contains("🩷 Pink wins!", text);
        Assert.Contains("46.2%", text);
        Assert.Contains("39.6%", text);
        Assert.Contains("14.2% unpainted", text);
        Assert.Contains("Votes: 🩷 3 · 💚 2", text);
        Assert.Contains("**Winners (3):** 🎲<@1> (+6), <@2> (+3), <@3> (+3)", text);
        Assert.DoesNotContain("more", text);
        Assert.DoesNotContain("Losers", text);
    }

    [Fact]
    public void Reveal_lists_losers_with_their_lost_points_and_the_top_three_standings()
    {
        var standings = new List<StandingEntry> { new(1, 14, 5), new(2, 11, 4), new(3, 9, 5), new(4, 2, 1) };
        var text = MessageText.Reveal(Round(),
            Summary(1, 2, [Out(1, 6, bet: true)], [Out(5, -3, bet: true), Out(6, 0), Out(7, 0, bet: true)], standings), maxListed: 50);

        Assert.Contains("**Losers (3):** 🎲<@5> (−3), <@6> (~), 🎲<@7> (~)", text);
        Assert.Contains("**Weekly standings**\n1. 🏆 <@1> 14 pts · 5 wins\n2. <@2> 11 pts · 4 wins\n3. <@3> 9 pts · 5 wins", text);
        Assert.DoesNotContain("<@4>", text); // only the top three
    }

    [Fact]
    public void Reveal_cuts_off_at_max_listed()
    {
        var winners = Enumerable.Range(1, 10).Select(i => Out((ulong)i, 3)).ToList();
        var text = MessageText.Reveal(Round(), Summary(10, 0, winners), maxListed: 4);
        Assert.Contains("<@4>", text);
        Assert.DoesNotContain("<@5>", text);
        Assert.Contains("…and 6 more", text);
    }

    [Fact]
    public void Reveal_stays_within_the_discord_limit()
    {
        var winners = Enumerable.Range(0, 500).Select(i => Out(1_234_567_890_123_456_000UL + (ulong)i, 6, bet: true)).ToList();
        var losers = Enumerable.Range(0, 500).Select(i => Out(1_234_567_890_123_457_000UL + (ulong)i, -3, bet: true)).ToList();
        var standings = new List<StandingEntry> { new(1_234_567_890_123_456_000UL, 99, 9), new(1_234_567_890_123_456_001UL, 98, 9), new(1_234_567_890_123_456_002UL, 97, 9) };

        foreach (var summary in new[] { Summary(500, 0, winners, standings: standings), Summary(500, 500, winners, losers, standings), Summary(0, 500, [], losers, standings) })
        {
            var text = MessageText.Reveal(Round(), summary, maxListed: 500);
            Assert.True(text.Length <= MessageText.DiscordMessageLimit, $"{text.Length} chars");
            Assert.Contains("more", text);
            Assert.Contains("**Weekly standings**", text);
        }
    }

    [Fact]
    public void Reveal_handles_no_votes_and_no_correct_guesses()
    {
        Assert.Contains("Nobody voted", MessageText.Reveal(Round(), Summary(0, 0, []), 50));
        Assert.Contains("Nobody guessed right", MessageText.Reveal(Round(), Summary(0, 4, [], [Out(1, 0)]), 50));
    }

    [Fact]
    public void Round_description_uses_discord_timestamps_and_lists_voters()
    {
        var open = MessageText.RoundDescription(Round(), V(1, 2, 3), closed: false);
        Assert.Contains("<t:1800000000:R>", open);
        Assert.Contains("**3 players have voted:** <@1>, <@2>, <@3>", open);
        Assert.Contains("No votes yet.", MessageText.RoundDescription(Round(), [], closed: false));
        Assert.Contains("**1 player has voted:** <@7>", MessageText.RoundDescription(Round(), V(7), closed: false));
        Assert.DoesNotContain("vote", MessageText.RoundDescription(Round(), null, closed: false).Replace("Cast your vote", ""));

        var closed = MessageText.RoundDescription(Round(), V(1, 2, 3), closed: true);
        Assert.Contains("closed", closed);
        Assert.Contains("<@1>, <@2>, <@3>", closed);
    }

    [Fact]
    public void Round_description_marks_bettors_and_shows_what_the_round_is_worth()
    {
        var round = Round();
        round.WinnerShare = 0.5130; // A rank = 3 points
        var text = MessageText.RoundDescription(round, [new Voter(1, true), new Voter(2, false), new Voter(3, true)], closed: false);

        Assert.Contains("**Rank A** · 3🔥\nWorth 3 points · bet: win +6 / lose −3\nResults <t:", text);
        Assert.Contains("**3 players have voted:** 🎲<@1>, <@2>, 🎲<@3>", text);

        round.WinnerShare = 0.5210; // C rank = 1 point
        Assert.Contains("Worth 1 point · bet: win +2 / lose −1", MessageText.RoundDescription(round, null, closed: true));
    }

    [Fact]
    public void Bet_replies_explain_the_stakes()
    {
        var round = Round();
        round.WinnerShare = 0.5130;
        Assert.Contains("win +6 / lose −3", MessageText.BetToggled(round, bet: true, hasVoted: true));
        Assert.DoesNotContain("only counts if you vote", MessageText.BetToggled(round, bet: true, hasVoted: true));
        Assert.Contains("only counts if you vote", MessageText.BetToggled(round, bet: true, hasVoted: false));
        Assert.Contains("off", MessageText.BetToggled(round, bet: false, hasVoted: true));
        Assert.Contains("Your bet is on: win +6 / lose −3", MessageText.VoteAccepted(round, Side.Left, bet: true));
        Assert.DoesNotContain("bet", MessageText.VoteAccepted(round, Side.Left));
    }

    [Fact]
    public void Week_summary_names_every_winner_and_is_skipped_when_nobody_scored()
    {
        Assert.Null(MessageText.WeekSummary("2026-11-02", []));
        Assert.Equal("🏁 **Week 45 is over!**\n🏆 <@1> wins with 41 pts and 17 wins.\nA new week starts now.",
            MessageText.WeekSummary("2026-11-02", [new StandingEntry(1, 41, 17)]));
        Assert.Contains("<@1>, <@2> win with 10 pts and 4 wins", MessageText.WeekSummary("2026-11-02", [new StandingEntry(1, 10, 4), new StandingEntry(2, 10, 4)]));
    }

    [Fact]
    public void Voter_list_is_cut_off_and_stays_within_the_embed_limit()
    {
        var ids = Enumerable.Range(1, 500).Select(i => new Voter(1_000_000_000_000_000_000UL + (ulong)i, false)).ToList();

        var capped = MessageText.RoundDescription(Round(), ids, closed: false, maxListed: 4);
        Assert.Contains("**500 players have voted:** ", capped);
        Assert.Equal(4, capped.Split("<@").Length - 1);
        Assert.EndsWith("…and 496 more", capped);

        var all = MessageText.RoundDescription(Round(), ids, closed: false, maxListed: 500);
        Assert.True(all.Length <= MessageText.EmbedDescriptionLimit, $"{all.Length} characters");
        Assert.Contains("more", all);
    }

    [Theory]
    [InlineData(0.5050, "**Rank X** · 5🔥")]
    [InlineData(0.5090, "**Rank S** · 4🔥")]
    [InlineData(0.5130, "**Rank A** · 3🔥")]
    [InlineData(0.5170, "**Rank B** · 2🔥")]
    [InlineData(0.5210, "**Rank C** · 1🔥")]
    public void Round_description_shows_the_difficulty_rank_open_and_closed(double winnerShare, string expected)
    {
        var round = Round();
        round.WinnerShare = winnerShare;
        string line = expected + "\n";
        Assert.Contains(line, MessageText.RoundDescription(round, V(1, 2, 3), closed: false));
        Assert.Contains(line, MessageText.RoundDescription(round, V(1, 2, 3), closed: true));
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
