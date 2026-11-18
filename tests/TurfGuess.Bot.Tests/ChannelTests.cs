using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Tests;

public class ChannelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ranked_channel_defaults_to_the_configured_channel_and_can_be_changed()
    {
        using var env = new TestEnvironment();
        Assert.Equal(1234UL, env.Settings.RankedChannelId());

        env.Settings.SetRankedChannelId(555);
        Assert.Equal(555UL, env.Settings.RankedChannelId());
        env.Settings.SetRankedChannelId(777);
        Assert.Equal(777UL, env.Settings.RankedChannelId());
    }

    [Fact]
    public async Task New_ranked_rounds_use_the_chosen_channel()
    {
        using var env = new TestEnvironment();
        env.Settings.SetRankedChannelId(555);

        await env.Rounds.AdvanceAsync(Now, default);

        Assert.Equal(555L, env.Repo.GetActive(RoundKind.Ranked)!.ChannelId);
    }

    [Fact]
    public async Task An_open_ranked_round_stays_in_its_channel_when_the_setting_changes()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Now, default);
        env.Settings.SetRankedChannelId(555);

        Assert.Equal(1234L, env.Repo.GetActive(RoundKind.Ranked)!.ChannelId);
    }

    [Fact]
    public async Task A_battle_is_posted_in_the_channel_it_was_started_in()
    {
        using var env = new TestEnvironment();
        env.Settings.SetRankedChannelId(555);

        var (round, started) = await env.Rounds.StartBattleAsync(1, false, Now, default, channelId: 999);

        Assert.True(started);
        Assert.Equal(999L, round.ChannelId);
    }

    [Fact]
    public async Task A_battle_without_a_channel_uses_the_configured_channel()
    {
        using var env = new TestEnvironment();
        var (round, _) = await env.Rounds.StartBattleAsync(1, false, Now, default);
        Assert.Equal(1234L, round.ChannelId);
    }

    [Fact]
    public void Help_hides_admin_commands_from_players()
    {
        string player = MessageText.Help(isAdmin: false);
        Assert.Contains("/turf battle", player);
        Assert.Contains("/turf help", player);
        Assert.DoesNotContain("/turf-admin", player);
    }

    [Fact]
    public void Help_lists_admin_commands_for_admins()
    {
        string admin = MessageText.Help(isAdmin: true);
        Assert.Contains("/turf battle", admin);
        foreach (var command in new[] { "status", "reveal-now", "remind", "reset-round", "reset-scores", "set-ranked-channel" })
            Assert.Contains("/turf-admin " + command, admin);
    }
}

public class BattleReadyPingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_starter_gets_a_ping_with_a_link_once_the_battle_is_posted()
    {
        using var env = new TestEnvironment();
        var (round, _) = await env.Rounds.StartBattleAsync(42, false, Now, default, channelId: 999);

        var (userId, card) = Assert.Single(env.Notifier.Cards);
        Assert.Equal(42UL, userId);
        Assert.Equal((ulong)round.MessageId!, card.LinkedMessageId);
        Assert.Equal(999UL, card.LinkedChannelId);
    }

    [Fact]
    public async Task An_opted_out_starter_gets_no_ping()
    {
        using var env = new TestEnvironment();
        env.Subscriptions.Unsubscribe(42, Now.ToUnixTimeSeconds());

        await env.Rounds.StartBattleAsync(42, false, Now, default);

        Assert.Empty(env.Notifier.Cards);
    }

    [Fact]
    public async Task A_refused_second_battle_sends_no_ping()
    {
        using var env = new TestEnvironment();
        await env.Rounds.StartBattleAsync(1, false, Now, default);
        env.Notifier.Cards.Clear();

        var (_, started) = await env.Rounds.StartBattleAsync(2, false, Now.AddMinutes(1), default);

        Assert.False(started);
        Assert.Empty(env.Notifier.Cards);
    }

    [Fact]
    public async Task Closed_DMs_do_not_break_the_battle()
    {
        using var env = new TestEnvironment();
        env.Notifier.Unreachable.Add(42);

        var (round, started) = await env.Rounds.StartBattleAsync(42, false, Now, default);

        Assert.True(started);
        Assert.Equal(RoundStatus.Open, round.Status);
    }
}

public class ReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static async Task<RoundRecord> OpenRankedAsync(TestEnvironment env, bool defaultLead = true)
    {
        if (defaultLead && env.Options.Game.ReminderMinutes == 0) env.Options.Game.ReminderMinutes = 60;
        await env.Rounds.AdvanceAsync(Now, default);
        return env.Repo.GetActive()!;
    }

    [Fact]
    public async Task The_reminder_is_posted_an_hour_before_the_reveal_and_only_once()
    {
        using var env = new TestEnvironment();
        var round = await OpenRankedAsync(env);

        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-61), default);
        Assert.Empty(env.Channel.Reminders);

        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-60), default);
        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-30), default);

        var (id, minutes) = Assert.Single(env.Channel.Reminders);
        Assert.Equal(round.Id, id);
        Assert.Equal(60, minutes);
    }

    [Fact]
    public async Task An_admin_can_post_the_reminder_now_and_the_automatic_one_still_follows()
    {
        using var env = new TestEnvironment();
        var round = await OpenRankedAsync(env);

        Assert.NotNull(await env.Rounds.RemindNowAsync(Now, default));
        var (id, minutes) = Assert.Single(env.Channel.Reminders);
        Assert.Equal(round.Id, id);
        Assert.Equal((int)Math.Ceiling((round.RevealAtTime - Now).TotalMinutes), minutes);

        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-60), default);
        Assert.Equal(2, env.Channel.Reminders.Count);
    }

    [Fact]
    public async Task Reminding_needs_an_open_ranked_round()
    {
        using var env = new TestEnvironment();
        Assert.Null(await env.Rounds.RemindNowAsync(Now, default)); // nothing posted yet

        var round = await OpenRankedAsync(env);
        Assert.Null(await env.Rounds.RemindNowAsync(round.RevealAtTime.AddMinutes(1), default)); // voting is over
        Assert.Empty(env.Channel.Reminders);
    }

    [Fact]
    public async Task The_scheduler_is_woken_at_the_reminder_time()
    {
        using var env = new TestEnvironment();
        var round = await OpenRankedAsync(env);

        var next = await env.Rounds.AdvanceAsync(Now, default);

        Assert.Equal(round.RevealAtTime.AddMinutes(-60), next);
    }

    [Fact]
    public async Task The_lead_time_is_configurable_and_zero_turns_it_off()
    {
        using var env = new TestEnvironment();
        env.Options.Game.ReminderMinutes = 15;
        var round = await OpenRankedAsync(env);
        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-16), default);
        Assert.Empty(env.Channel.Reminders);
        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-15), default);
        Assert.Equal(15, Assert.Single(env.Channel.Reminders).MinutesLeft);

        using var off = new TestEnvironment();
        var r2 = await OpenRankedAsync(off, defaultLead: false);
        await off.Rounds.AdvanceAsync(r2.RevealAtTime.AddMinutes(-1), default);
        Assert.Empty(off.Channel.Reminders);
    }

    [Fact]
    public async Task A_round_that_opens_with_less_time_than_the_lead_gets_no_reminder()
    {
        using var env = new TestEnvironment();
        env.Options.Game.ReminderMinutes = 24 * 60; // longer than the round
        var round = await OpenRankedAsync(env);

        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-5), default);

        Assert.Empty(env.Channel.Reminders);
    }

    [Fact]
    public async Task A_late_reminder_counts_the_minutes_really_left()
    {
        using var env = new TestEnvironment();
        var round = await OpenRankedAsync(env);

        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-20), default); // bot was down at the due time

        Assert.Equal(20, Assert.Single(env.Channel.Reminders).MinutesLeft);
    }

    [Fact]
    public async Task A_failing_reminder_does_not_hold_up_the_round()
    {
        using var env = new TestEnvironment();
        env.Channel.FailReminders = true;
        var round = await OpenRankedAsync(env);

        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(-30), default);
        await env.Rounds.AdvanceAsync(round.RevealAtTime.AddMinutes(1), default);

        Assert.Equal(1, env.Channel.RevealPosts);
    }

    [Fact]
    public void The_text_and_validation()
    {
        var round = new RoundRecord { Id = 12, Number = 12 };
        Assert.Equal("**It's now or never!**\n- Ranked Battle #12 has 60 minutes left.\n- Nobody has voted yet.\n[**Vote now!**](https://x/y)", MessageText.Reminder(round, 60, "https://x/y"));
        Assert.Contains("1 minute left", MessageText.Reminder(round, 1, "u"));

        Assert.Equal("**It's now or never!**\n- Ranked Battle #12 has 60 minutes left.\n- 8 people have voted already, with one color favoured over another.\n" +
                     "- Last time, 7 people voted. 43% were right.\n[**Vote now!**](https://x/y)",
            MessageText.Reminder(round, 60, "https://x/y", leftVotes: 3, rightVotes: 5, previous: (7, 3)));

        // One voter: no split.
        var single = MessageText.Reminder(round, 5, "u", 1, 0);
        Assert.Contains("- 1 person has voted already.\n", single);
        Assert.DoesNotContain("favoured", single);

        // Bands, whichever team leads: up to 55% roughly equal, 56%+ favoured, 86%+ heavily.
        foreach (var (a, b) in new[] { (5, 5), (11, 9), (6, 4), (4, 6), (17, 3), (3, 17) })
        {
            string text = MessageText.Reminder(round, 5, "u", a, b);
            int major = (int)Math.Round(100.0 * Math.Max(a, b) / (a + b), MidpointRounding.AwayFromZero);
            string expected = major >= 86 ? "favoured heavily" : major >= 56 ? "favoured over another" : "favoured roughly equally";
            Assert.Contains(expected, text);
        }
        Assert.Contains("with both colors favoured roughly equally", MessageText.Reminder(round, 5, "u", 11, 9));
        Assert.Contains("with one color favoured over another", MessageText.Reminder(round, 5, "u", 3, 2));
        Assert.Contains("with one color favoured heavily", MessageText.Reminder(round, 5, "u", 0, 7));

        Assert.Contains("- Last time, 1 person voted, and they were right.\n", MessageText.Reminder(round, 5, "u", previous: (1, 1)));
        Assert.Contains("- Last time, 1 person voted, and they were wrong.\n", MessageText.Reminder(round, 5, "u", previous: (1, 0)));

        var o = new TurfGuess.Bot.Options.BotOptions { Discord = { Token = "t", ChannelId = 1 }, Game = { ReminderMinutes = -1 } };
        Assert.Contains(TurfGuess.Bot.Options.BotOptionsValidator.Validate(o), e => e.Contains("ReminderMinutes"));
    }
}
