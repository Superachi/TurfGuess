using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

/// <summary>🔫 Regular Battles: started by a player, 15 minutes, never scored, one at a time.</summary>
public class BattleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Starting_a_battle_posts_it_and_it_ends_after_the_battle_length()
    {
        using var env = new TestEnvironment();

        var (round, started) = await env.Rounds.StartBattleAsync(42, showVotes: false, Now, default);

        Assert.True(started);
        Assert.Equal(RoundKind.Regular, round.Kind);
        Assert.Equal(RoundStatus.Open, round.Status);
        Assert.Equal(42, round.StartedBy);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), round.RevealAtTime);
        Assert.Equal(1, env.Channel.RoundPosts);
    }

    [Fact]
    public async Task A_battle_cannot_be_started_while_one_is_running()
    {
        using var env = new TestEnvironment();
        var (first, _) = await env.Rounds.StartBattleAsync(1, showVotes: false, Now, default);

        var (second, started) = await env.Rounds.StartBattleAsync(2, showVotes: true, Now.AddMinutes(5), default);

        Assert.False(started);
        Assert.Equal(first.Id, second.Id);
        Assert.False(second.ShowVotes); // the running battle is unchanged
        Assert.Equal(1, env.Channel.RoundPosts);
    }

    [Fact]
    public async Task A_new_battle_can_start_once_the_last_one_is_revealed()
    {
        using var env = new TestEnvironment();
        var (first, _) = await env.Rounds.StartBattleAsync(1, false, Now, default);

        await env.Rounds.AdvanceAsync(Now.AddMinutes(15), default);
        Assert.Equal(RoundStatus.Done, env.Repo.Get(first.Id)!.Status);

        var (second, started) = await env.Rounds.StartBattleAsync(2, false, Now.AddMinutes(16), default);
        Assert.True(started);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Ranked_rounds_and_battles_keep_separate_displayed_counts()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Now, default); // creates the first ranked round
        var ranked = env.Repo.GetActive(RoundKind.Ranked)!;

        var (first, _) = await env.Rounds.StartBattleAsync(1, false, Now, default);
        await env.Rounds.AdvanceAsync(Now.AddMinutes(15), default);
        var (second, _) = await env.Rounds.StartBattleAsync(2, false, Now.AddMinutes(16), default);

        Assert.Equal(1, ranked.DisplayNumber);
        Assert.Equal(1, first.DisplayNumber);
        Assert.Equal(2, second.DisplayNumber); // the ranked round in between doesn't count
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task The_battle_stays_open_until_its_time_is_up()
    {
        using var env = new TestEnvironment();
        var (round, _) = await env.Rounds.StartBattleAsync(1, false, Now, default);

        await env.Rounds.AdvanceAsync(Now.AddMinutes(14), default);

        Assert.Equal(RoundStatus.Open, env.Repo.Get(round.Id)!.Status);
        Assert.Equal(0, env.Channel.RevealPosts);
        Assert.Equal(VoteResult.Accepted, env.Votes.Cast(round.Id, 7, "gus", Side.Left));
    }

    [Fact]
    public async Task Revealing_now_ends_the_battle_without_scoring_anything()
    {
        using var env = new TestEnvironment();
        var (round, _) = await env.Rounds.StartBattleAsync(1, false, Now, default);
        env.Votes.Cast(round.Id, 7, "gus", round.WinnerSide);
        env.Votes.Cast(round.Id, 8, "hal", round.WinnerSide.Opposite());

        Assert.True(env.Rounds.RevealNow(Now.AddMinutes(2), RoundKind.Regular));
        await env.Rounds.AdvanceAsync(Now.AddMinutes(2), default);

        Assert.Equal(RoundStatus.Done, env.Repo.Get(round.Id)!.Status);
        var (_, summary) = Assert.Single(env.Channel.Reveals);
        Assert.Equal([7UL], summary.Winners.Select(w => w.UserId));
        Assert.Equal([8UL], summary.Losers.Select(l => l.UserId));
        Assert.Empty(summary.Standings);
        Assert.Empty(env.Scores.GetResults(round.Id));
        Assert.Empty(env.Scores.Standings(Weekly.WeekStart(round.RevealAtTime, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"))));
        Assert.Equal(VoteResult.Closed, env.Votes.Cast(round.Id, 9, "ivy", Side.Left));
    }

    [Fact]
    public async Task Revealing_now_does_nothing_without_a_battle()
    {
        using var env = new TestEnvironment();
        Assert.False(env.Rounds.RevealNow(Now, RoundKind.Regular));

        await env.Rounds.AdvanceAsync(Now, default); // opens the ranked round
        Assert.False(env.Rounds.RevealNow(Now, RoundKind.Regular)); // the ranked round is not a battle
        Assert.Equal(RoundStatus.Open, env.Repo.GetActive()!.Status);
    }

    [Fact]
    public async Task Ranked_and_regular_rounds_run_side_by_side_without_touching_each_other()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Now, default);
        var ranked = env.Repo.GetActive()!;

        var (battle, started) = await env.Rounds.StartBattleAsync(1, false, Now, default);
        Assert.True(started);
        Assert.Equal(ranked.Id, env.Repo.GetActive()!.Id);
        Assert.Equal(battle.Id, env.Repo.GetActive(RoundKind.Regular)!.Id);

        env.Votes.Cast(ranked.Id, 5, "eve", ranked.WinnerSide);
        env.Votes.Cast(battle.Id, 6, "fay", battle.WinnerSide);

        // The battle ends first. The ranked round is still open and nobody has points.
        await env.Rounds.AdvanceAsync(Now.AddMinutes(15), default);
        Assert.Equal(RoundStatus.Done, env.Repo.Get(battle.Id)!.Status);
        Assert.Equal(RoundStatus.Open, env.Repo.Get(ranked.Id)!.Status);
        Assert.Empty(env.Scores.GetResults(battle.Id));

        // Then the ranked round: only its own voter scores.
        await env.Rounds.AdvanceAsync(ranked.RevealAtTime, default);
        Assert.Single(env.Scores.GetResults(ranked.Id));
        Assert.Empty(env.Scores.GetResults(battle.Id));
    }

    [Fact]
    public async Task A_battle_does_not_count_as_the_first_ranked_round()
    {
        using var env = new TestEnvironment();
        await env.Rounds.StartBattleAsync(1, false, Now, default);
        Assert.False(env.Repo.AnyRounds());
        Assert.Null(env.Repo.GetActive());
    }

    [Fact]
    public async Task Public_votes_list_each_voters_team()
    {
        using var env = new TestEnvironment();
        var (round, _) = await env.Rounds.StartBattleAsync(1, showVotes: true, Now, default);
        env.Votes.Cast(round.Id, 7, "gus", Side.Left);
        env.Votes.Cast(round.Id, 8, "hal", Side.Right);

        var voters = env.Repo.GetVotersFor(env.Repo.Get(round.Id)!);

        Assert.Equal([new Voter(7, false, Side.Left), new Voter(8, false, Side.Right)], voters);
    }

    [Fact]
    public void Ranked_voters_never_carry_a_choice()
    {
        using var env = new TestEnvironment();
        Assert.All(env.Repo.GetVoters(1), v => Assert.Null(v.Choice));
    }

    // ---- message text -------------------------------------------------------------------------

    private static RoundRecord Round(RoundKind kind, bool showVotes = false) => new()
    {
        Id = 13, Kind = kind, ShowVotes = showVotes, StartedBy = 99, RevealAt = Now.ToUnixTimeSeconds(),
        LeftName = "Reds", LeftEmoji = "🔴", RightName = "Blues", RightEmoji = "🔵",
        Winner = "left", WinnerShare = 0.52, ShareLeft = 0.4, ShareRight = 0.37, Unpainted = 0.23,
    };

    [Fact]
    public void Titles_and_footers_tell_the_two_kinds_apart()
    {
        Assert.Equal("Turf Guess · ⚓ Ranked Battle · Round #13", MessageText.RoundTitle("Turf Guess", Round(RoundKind.Ranked)));
        Assert.Equal("Turf Guess · 🔫 Regular Battle · Round #13", MessageText.RoundTitle("Turf Guess", Round(RoundKind.Regular)));
        Assert.Equal("Round #13 · Results", MessageText.RevealFooter(Round(RoundKind.Ranked))); // recovery relies on this text
        Assert.Equal("Regular Battle #13 · Results", MessageText.RevealFooter(Round(RoundKind.Regular)));
    }

    [Fact]
    public void Difficulty_is_fire_for_ranked_and_fish_for_regular()
    {
        string ranked = MessageText.DifficultyLine(Round(RoundKind.Ranked));
        string regular = MessageText.DifficultyLine(Round(RoundKind.Regular));
        Assert.Contains("🔥", ranked);
        Assert.DoesNotContain("🐟", ranked);
        Assert.Contains("🐟", regular);
        Assert.DoesNotContain("🔥", regular);
    }

    [Fact]
    public void Battle_message_says_there_are_no_points_and_offers_no_bet()
    {
        string text = MessageText.RoundDescription(Round(RoundKind.Regular), [], closed: false);
        Assert.Contains("no points", text);
        Assert.Contains("Started by <@99>", text);
        Assert.DoesNotContain("bet:", text);
        Assert.DoesNotContain("votes are public", text);
    }

    [Fact]
    public void Public_votes_are_listed_per_team_and_private_ones_are_not()
    {
        var voters = new[] { new Voter(1, false, Side.Left), new Voter(2, false, Side.Right), new Voter(3, false, Side.Left) };

        string open = MessageText.RoundDescription(Round(RoundKind.Regular, showVotes: true), voters, closed: false);
        Assert.Contains("votes are public", open);
        Assert.Contains("🔴 **Reds** (2): <@1>, <@3>", open);
        Assert.Contains("🔵 **Blues** (1): <@2>", open);

        string hidden = MessageText.RoundDescription(Round(RoundKind.Regular), voters, closed: false);
        Assert.Contains("3 players have voted", hidden);
        Assert.DoesNotContain("**Reds**", hidden);
    }

    [Fact]
    public void Battle_reveal_has_no_points_and_no_standings()
    {
        var summary = new RevealSummary(1, 1, [new PlayerOutcome(1, false, 0)], [new PlayerOutcome(2, false, 0)],
            [new StandingEntry(1, 5, 1)]);

        string text = MessageText.Reveal(Round(RoundKind.Regular), summary, 50);

        Assert.Contains("Reds wins!", text);
        Assert.Contains("✅ **Correct (1):** <@1>", text);
        Assert.Contains("❌ **Wrong (1):** <@2>", text);
        Assert.Contains("no points awarded", text);
        Assert.DoesNotContain("Weekly standings", text);
        Assert.DoesNotContain("(+", text);
    }

    [Fact]
    public void Battle_reveal_with_no_votes_says_so()
    {
        var summary = new RevealSummary(0, 0, [], [], []);
        Assert.Contains("Nobody voted", MessageText.Reveal(Round(RoundKind.Regular), summary, 50));
    }

    [Fact]
    public void Public_vote_confirmation_warns_that_the_vote_is_public()
    {
        Assert.Contains("public", MessageText.VoteAccepted(Round(RoundKind.Regular, showVotes: true), Side.Left));
        Assert.DoesNotContain("public", MessageText.VoteAccepted(Round(RoundKind.Regular), Side.Left));
    }

    [Fact]
    public void Battle_length_must_be_sensible()
    {
        var options = new TurfGuess.Bot.Options.BotOptions
        {
            Discord = { Token = "t", ChannelId = 1 },
            Game = { BattleLength = TimeSpan.Zero },
        };
        Assert.Contains(TurfGuess.Bot.Options.BotOptionsValidator.Validate(options), e => e.Contains("BattleLength"));
    }

    [Fact]
    public void An_older_database_with_one_active_round_index_is_upgraded()
    {
        string path = Path.Combine(Path.GetTempPath(), "turfguess-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE rounds (id INTEGER PRIMARY KEY AUTOINCREMENT, channel_id INTEGER NOT NULL,
                        status TEXT NOT NULL, message_id INTEGER, reveal_message_id INTEGER, seed INTEGER NOT NULL,
                        generator_version INTEGER NOT NULL, image_path TEXT NOT NULL DEFAULT '', reveal_image_path TEXT NOT NULL DEFAULT '',
                        created_at INTEGER NOT NULL, posted_at INTEGER, reveal_at INTEGER NOT NULL, left_name TEXT NOT NULL,
                        left_color TEXT NOT NULL, left_emoji TEXT NOT NULL, right_name TEXT NOT NULL, right_color TEXT NOT NULL,
                        right_emoji TEXT NOT NULL, winner TEXT NOT NULL, winner_share REAL NOT NULL, share_left REAL NOT NULL,
                        share_right REAL NOT NULL, unpainted REAL NOT NULL);
                    CREATE UNIQUE INDEX one_active_round ON rounds (status <> 'Done') WHERE status <> 'Done';
                    INSERT INTO rounds (channel_id, status, seed, generator_version, created_at, reveal_at, left_name, left_color,
                        left_emoji, right_name, right_color, right_emoji, winner, winner_share, share_left, share_right, unpainted)
                    VALUES (1, 'Open', 5, 1, 0, 100, 'a', '#fff', 'x', 'b', '#000', 'y', 'left', 0.5, 0.4, 0.4, 0.2);
                    """;
                cmd.ExecuteNonQuery();
            }

            var db = new Database(path);
            db.EnsureSchema();
            db.EnsureSchema(); // safe to repeat
            var repo = new RoundRepository(db);

            var old = repo.GetActive()!;
            Assert.Equal(RoundKind.Ranked, old.Kind);
            Assert.False(old.ShowVotes);

            var battle = new RoundRecord
            {
                Kind = RoundKind.Regular, Status = RoundStatus.Pending, ChannelId = 1, Seed = 6, CreatedAt = 1, RevealAt = 2,
                LeftName = "a", LeftColor = "#fff", LeftEmoji = "x", RightName = "b", RightColor = "#000", RightEmoji = "y",
                Winner = "left",
            };
            repo.Insert(battle); // allowed next to the open ranked round
            Assert.ThrowsAny<Exception>(() => repo.Insert(battle)); // but not a second battle
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); } catch (IOException) { }
        }
    }
}
