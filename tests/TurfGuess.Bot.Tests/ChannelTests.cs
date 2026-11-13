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
        foreach (var command in new[] { "status", "reveal-now", "reset-round", "reset-scores", "set-ranked-channel" })
            Assert.Contains("/turf-admin " + command, admin);
    }
}
