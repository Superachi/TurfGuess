using Dapper;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Data;

/// <summary>All SQL for rounds and votes. Small, hand-written queries with Dapper.</summary>
public sealed class RoundRepository(Database db)
{
    private const string RoundColumns = """
        id, number, channel_id, status, message_id, reveal_message_id, seed, generator_version,
        image_path, reveal_image_path, created_at, posted_at, reveal_at,
        left_name, left_color, left_emoji, right_name, right_color, right_emoji,
        winner, winner_share, share_left, share_right, unpainted, kind, show_votes, started_by
        """;

    /// <summary>Whether any ranked round exists (regular battles don't count).</summary>
    public bool AnyRounds()
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM rounds WHERE kind = 'Ranked'") > 0;
    }

    /// <summary>The one round of this kind that isn't Done, if any.</summary>
    public RoundRecord? GetActive(RoundKind kind = RoundKind.Ranked)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<RoundRecord>(
            $"SELECT {RoundColumns} FROM rounds WHERE status <> 'Done' AND kind = @kind", new { kind = kind.ToString() });
    }

    public RoundRecord? Get(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<RoundRecord>($"SELECT {RoundColumns} FROM rounds WHERE id = @id", new { id });
    }

    /// <summary>Inserts a new Pending round and returns its id.</summary>
    public long Insert(RoundRecord r)
    {
        using var c = db.Open();
        // The displayed number is the next one for this kind, so ranked and regular rounds count separately.
        var (id, number) = c.QuerySingle<(long Id, long Number)>("""
            INSERT INTO rounds (number, channel_id, status, seed, generator_version, image_path, reveal_image_path,
                                created_at, reveal_at, left_name, left_color, left_emoji,
                                right_name, right_color, right_emoji, winner, winner_share,
                                share_left, share_right, unpainted, kind, show_votes, started_by)
            VALUES ((SELECT COALESCE(MAX(number), 0) + 1 FROM rounds WHERE kind = @Kind),
                    @ChannelId, 'Pending', @Seed, @GeneratorVersion, @ImagePath, @RevealImagePath,
                    @CreatedAt, @RevealAt, @LeftName, @LeftColor, @LeftEmoji,
                    @RightName, @RightColor, @RightEmoji, @Winner, @WinnerShare,
                    @ShareLeft, @ShareRight, @Unpainted, @Kind, @ShowVotes, @StartedBy)
            RETURNING id, number;
            """, new
        {
            r.ChannelId, r.Seed, r.GeneratorVersion, r.ImagePath, r.RevealImagePath, r.CreatedAt, r.RevealAt,
            r.LeftName, r.LeftColor, r.LeftEmoji, r.RightName, r.RightColor, r.RightEmoji,
            r.Winner, r.WinnerShare, r.ShareLeft, r.ShareRight, r.Unpainted,
            Kind = r.Kind.ToString(), r.ShowVotes, r.StartedBy,
        });
        r.Number = number;
        return id;
    }

    public void SetImagePaths(long id, string imagePath, string revealImagePath)
    {
        using var c = db.Open();
        c.Execute("UPDATE rounds SET image_path = @imagePath, reveal_image_path = @revealImagePath WHERE id = @id",
            new { id, imagePath, revealImagePath });
    }

    /// <summary>Pending → Open, once the round message exists.</summary>
    public void MarkOpen(long id, long messageId, long postedAt)
    {
        using var c = db.Open();
        c.Execute("UPDATE rounds SET status = 'Open', message_id = @messageId, posted_at = @postedAt WHERE id = @id AND status = 'Pending'",
            new { id, messageId, postedAt });
    }

    /// <summary>Open → Revealed. This is what closes voting.</summary>
    public void MarkRevealed(long id)
    {
        using var c = db.Open();
        c.Execute("UPDATE rounds SET status = 'Revealed' WHERE id = @id AND status = 'Open'", new { id });
    }

    /// <summary>Revealed → Done, once the reveal message exists.</summary>
    public void MarkDone(long id, long revealMessageId)
    {
        using var c = db.Open();
        c.Execute("UPDATE rounds SET status = 'Done', reveal_message_id = @revealMessageId WHERE id = @id AND status = 'Revealed'",
            new { id, revealMessageId });
    }

    /// <summary>
    /// Ends the round without a reveal (admin reset). It becomes Done, so its buttons stop working and
    /// a new round is created next. Its id is never reused, and nothing is scored for it.
    /// </summary>
    public bool Abandon(long id)
    {
        using var c = db.Open();
        return c.Execute("UPDATE rounds SET status = 'Done' WHERE id = @id AND status <> 'Done'", new { id }) > 0;
    }

    /// <summary>Moves an open round's reveal time (admin "reveal now").</summary>
    public bool SetRevealAt(long id, long revealAt)
    {
        using var c = db.Open();
        return c.Execute("UPDATE rounds SET reveal_at = @revealAt WHERE id = @id AND status = 'Open'", new { id, revealAt }) > 0;
    }

    /// <summary>
    /// Records or changes a vote, but only while the round is Open, in one statement (DESIGN.md §2).
    /// Returns false when voting is closed or the round doesn't exist.
    /// </summary>
    public bool TryUpsertVote(long roundId, long userId, string userName, Side choice, long votedAt)
    {
        using var c = db.Open();
        int rows = c.Execute("""
            INSERT INTO votes (round_id, user_id, user_name, choice, voted_at)
            SELECT @roundId, @userId, @userName, @choice, @votedAt
            WHERE EXISTS (SELECT 1 FROM rounds WHERE id = @roundId AND status = 'Open')
            ON CONFLICT (round_id, user_id) DO UPDATE
                SET choice = excluded.choice, user_name = excluded.user_name, voted_at = excluded.voted_at;
            """, new { roundId, userId, userName, choice = SideCodec.Format(choice), votedAt });
        return rows > 0;
    }

    /// <summary>A player's current vote in a round, if any.</summary>
    public VoteRecord? GetVote(long roundId, long userId)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<VoteRecord>(
            "SELECT round_id, user_id, user_name, choice, voted_at FROM votes WHERE round_id = @roundId AND user_id = @userId",
            new { roundId, userId });
    }

    public IReadOnlyList<VoteRecord> GetVotes(long roundId)
    {
        using var c = db.Open();
        return c.Query<VoteRecord>(
            "SELECT round_id, user_id, user_name, choice, voted_at FROM votes WHERE round_id = @roundId ORDER BY voted_at, user_id",
            new { roundId }).AsList();
    }

    /// <summary>
    /// Everyone who voted in a round, for the round message, sorted by name. Not by vote time:
    /// changing a vote updates voted_at, so that order would show who switched sides.
    /// </summary>
    public IReadOnlyList<Voter> GetVoters(long roundId)
    {
        using var c = db.Open();
        return c.Query<(long UserId, long Bet)>("""
            SELECT v.user_id AS UserId,
                   EXISTS (SELECT 1 FROM bets b WHERE b.round_id = v.round_id AND b.user_id = v.user_id) AS Bet
            FROM votes v WHERE v.round_id = @roundId
            ORDER BY v.user_name COLLATE NOCASE, v.user_id
            """, new { roundId }).Select(r => new Voter((ulong)r.UserId, r.Bet != 0)).ToList();
    }

    /// <summary>The voters to show on a round's message: with their team for public-vote battles, otherwise names (and bets) only.</summary>
    public IReadOnlyList<Voter> GetVotersFor(RoundRecord round) =>
        round.ShowVotes ? GetVotersWithChoice(round.Id) : GetVoters(round.Id);

    /// <summary>Everyone who voted, with the team they picked (regular battles with public votes), sorted by name.</summary>
    public IReadOnlyList<Voter> GetVotersWithChoice(long roundId)
    {
        using var c = db.Open();
        return c.Query<(long UserId, string Choice)>("""
            SELECT user_id AS UserId, choice AS Choice FROM votes WHERE round_id = @roundId
            ORDER BY user_name COLLATE NOCASE, user_id
            """, new { roundId }).Select(r => new Voter((ulong)r.UserId, false, SideCodec.Parse(r.Choice))).ToList();
    }

    /// <summary>
    /// Flips a player's bet on a round, but only while the round is Open. Returns the new state
    /// (true = betting), or null when voting is closed or the round doesn't exist.
    /// </summary>
    public bool? ToggleBet(long roundId, long userId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        bool open = c.ExecuteScalar<long>("SELECT COUNT(*) FROM rounds WHERE id = @roundId AND status = 'Open'",
            new { roundId }, tx) > 0;
        if (!open) return null;

        int removed = c.Execute("DELETE FROM bets WHERE round_id = @roundId AND user_id = @userId", new { roundId, userId }, tx);
        if (removed == 0)
            c.Execute("INSERT INTO bets (round_id, user_id) VALUES (@roundId, @userId)", new { roundId, userId }, tx);
        tx.Commit();
        return removed == 0;
    }

    /// <summary>Turns a player's bet on (only while the round is Open). Returns true when a bet was added.</summary>
    public bool EnsureBet(long roundId, long userId)
    {
        using var c = db.Open();
        return c.Execute("""
            INSERT OR IGNORE INTO bets (round_id, user_id)
            SELECT @roundId, @userId WHERE EXISTS (SELECT 1 FROM rounds WHERE id = @roundId AND status = 'Open')
            """, new { roundId, userId }) > 0;
    }

    public bool HasBet(long roundId, long userId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM bets WHERE round_id = @roundId AND user_id = @userId",
            new { roundId, userId }) > 0;
    }

    /// <summary>Who placed a bet on the round (voters or not).</summary>
    public IReadOnlySet<long> GetBettorIds(long roundId)
    {
        using var c = db.Open();
        return c.Query<long>("SELECT user_id FROM bets WHERE round_id = @roundId", new { roundId }).ToHashSet();
    }

    public int CountVotes(long roundId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM votes WHERE round_id = @roundId", new { roundId });
    }
}
