using Dapper;
using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Data;

/// <summary>One row of <c>round_results</c>.</summary>
public sealed class ResultRecord
{
    public long RoundId { get; set; }
    public long UserId { get; set; }
    public string WeekStart { get; set; } = "";
    public long Correct { get; set; }
    public long Bet { get; set; }
    public long Delta { get; set; }
}

/// <summary>A voter at the moment of the reveal, as input for scoring.</summary>
public sealed record ScoringVote(long UserId, bool Correct, bool Bet);

/// <summary>All SQL for weekly points, wins and trophies.</summary>
public sealed class ScoreRepository(Database db)
{
    public IReadOnlyList<ResultRecord> GetResults(long roundId)
    {
        using var c = db.Open();
        return c.Query<ResultRecord>(
            "SELECT round_id, user_id, week_start, correct, bet, delta FROM round_results WHERE round_id = @roundId ORDER BY rowid",
            new { roundId }).AsList();
    }

    /// <summary>
    /// Scores a revealed round, once. The weekly score never drops below 0, so each result is applied
    /// in order on top of the player's score so far this week. Calling it again for the same round
    /// changes nothing and returns the stored results (a crash between scoring and the reveal is safe).
    /// </summary>
    public IReadOnlyList<ResultRecord> ScoreRound(long roundId, string weekStart, int points, IReadOnlyList<ScoringVote> votes)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();

        bool done = c.ExecuteScalar<long>("SELECT COUNT(*) FROM round_results WHERE round_id = @roundId", new { roundId }, tx) > 0;
        if (!done)
        {
            foreach (var v in votes)
            {
                long current = c.ExecuteScalar<long>(
                    "SELECT COALESCE(SUM(delta), 0) FROM round_results WHERE week_start = @weekStart AND user_id = @UserId",
                    new { weekStart, v.UserId }, tx);
                int delta = Weekly.AppliedDelta((int)current, Weekly.RawDelta(points, v.Correct, v.Bet));
                c.Execute("""
                    INSERT INTO round_results (round_id, user_id, week_start, correct, bet, delta)
                    VALUES (@roundId, @UserId, @weekStart, @correct, @bet, @delta)
                    """, new { roundId, v.UserId, weekStart, correct = v.Correct ? 1 : 0, bet = v.Bet ? 1 : 0, delta }, tx);
            }
        }

        tx.Commit();
        return GetResults(roundId);
    }

    /// <summary>The week's standings, best first: points, then wins (correct guesses), then id for a stable order.</summary>
    public IReadOnlyList<StandingEntry> Standings(string weekStart)
    {
        using var c = db.Open();
        return c.Query<(long UserId, long Points, long Wins)>("""
            SELECT user_id AS UserId, SUM(delta) AS Points, SUM(correct) AS Wins
            FROM round_results WHERE week_start = @weekStart
            GROUP BY user_id
            ORDER BY Points DESC, Wins DESC, user_id
            """, new { weekStart }).Select(r => new StandingEntry((ulong)r.UserId, (int)r.Points, (int)r.Wins)).ToList();
    }

    /// <summary>Weeks before <paramref name="weekStart"/> that have results but no summary yet, oldest first.</summary>
    public IReadOnlyList<string> UnclosedWeeksBefore(string weekStart)
    {
        using var c = db.Open();
        return c.Query<string>("""
            SELECT DISTINCT week_start FROM round_results
            WHERE week_start < @weekStart AND week_start NOT IN (SELECT week_start FROM closed_weeks)
            ORDER BY week_start
            """, new { weekStart }).AsList();
    }

    /// <summary>Marks a week done and records its winners for the trophy tally.</summary>
    public void CloseWeek(string weekStart, IReadOnlyList<StandingEntry> winners)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        c.Execute("INSERT OR IGNORE INTO closed_weeks (week_start) VALUES (@weekStart)", new { weekStart }, tx);
        foreach (var w in winners)
            c.Execute("INSERT OR IGNORE INTO weekly_winners (week_start, user_id, points, wins) VALUES (@weekStart, @userId, @points, @wins)",
                new { weekStart, userId = (long)w.UserId, points = w.Points, wins = w.Wins }, tx);
        tx.Commit();
    }

    /// <summary>How many weeks a player has won. Tallied for later; not shown anywhere yet.</summary>
    public int TrophyCount(ulong userId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM weekly_winners WHERE user_id = @userId", new { userId = (long)userId });
    }
}
