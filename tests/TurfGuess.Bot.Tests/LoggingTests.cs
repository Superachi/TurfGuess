using Dapper;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

public class LoggingTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset Local(int d, int h)
    {
        var dt = new DateTime(2026, 11, d, h, 0, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public async Task Posting_a_round_is_logged_with_channel_colors_and_answer()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var r = env.Repo.GetActive()!;

        Assert.Contains(env.Log.Messages, m => m.Contains($"Round {r.Id}: created") && m.Contains("Answer:") && m.Contains(r.LeftName));
        Assert.Contains(env.Log.Messages, m => m.Contains($"Round {r.Id}: posted in channel 1234") && m.Contains("Voting is open until 2026-11-11 09:00"));
    }

    [Fact]
    public async Task Votes_are_logged_with_who_and_what()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var r = env.Repo.GetActive()!;
        string left = r.TeamName(Side.Left), right = r.TeamName(Side.Right);

        env.Votes.Cast(r.Id, 7, "alice", Side.Left);
        env.Votes.Cast(r.Id, 7, "alice", Side.Right);
        env.Votes.Cast(r.Id, 7, "alice", Side.Right);

        var log = env.Log.Messages;
        Assert.Contains(log, m => m == $"Round {r.Id}: alice (7) voted {left}.");
        Assert.Contains(log, m => m == $"Round {r.Id}: alice (7) changed their vote from {left} to {right}.");
        Assert.Contains(log, m => m == $"Round {r.Id}: alice (7) voted {right} again (no change).");
    }

    [Fact]
    public async Task Rejected_late_votes_are_logged()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var first = env.Repo.GetActive()!;
        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        env.Votes.Cast(first.Id, 8, "bob", Side.Left);
        Assert.Contains(env.Log.Messages, m => m.Contains($"Round {first.Id}: bob (8) clicked left after voting closed; rejected."));
    }

    [Fact]
    public async Task The_reveal_is_logged_with_full_stats_and_correct_guessers()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var r = env.Repo.GetActive()!;
        var winner = r.WinnerSide;
        env.Votes.Cast(r.Id, 1, "alice", winner);
        env.Votes.Cast(r.Id, 2, "bob", winner.Opposite());
        env.Votes.Cast(r.Id, 3, "carol", winner);

        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        var log = env.Log.Messages;
        Assert.Contains(log, m => m.Contains($"Round {r.Id}: voting closed with 3 votes."));
        var reveal = Assert.Single(log, m => m.Contains($"Round {r.Id}: revealed"));
        Assert.Contains($"{r.TeamName(winner)} wins with", reveal);
        Assert.Contains("unpainted", reveal);
        Assert.Contains($"Votes: 3 ({r.TeamName(winner)} 2, {r.TeamName(winner.Opposite())} 1)", reveal);
        Assert.Contains("Correct: 2/3 (67%): alice, carol.", reveal);
    }

    [Fact]
    public void Old_databases_get_the_user_name_column()
    {
        using var env = new TestEnvironment();
        string path = Path.Combine(env.Folder, "old.db");
        var db = new Database(path);
        using (var c = db.Open())
            c.Execute("CREATE TABLE votes (round_id INTEGER NOT NULL, user_id INTEGER NOT NULL, choice TEXT NOT NULL, voted_at INTEGER NOT NULL, PRIMARY KEY (round_id, user_id));");

        db.EnsureSchema();

        using var check = db.Open();
        Assert.Contains("user_name", check.Query<string>("SELECT name FROM pragma_table_info('votes')"));
    }
}
