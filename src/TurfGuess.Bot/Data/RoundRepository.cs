using Dapper;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Data;

/// <summary>All SQL for rounds and votes. Small, hand-written queries with Dapper.</summary>
public sealed class RoundRepository(Database db)
{
    private const string RoundColumns = """
        id, channel_id, status, message_id, reveal_message_id, seed, generator_version,
        image_path, reveal_image_path, created_at, posted_at, reveal_at,
        left_name, left_color, left_emoji, right_name, right_color, right_emoji,
        winner, winner_share, share_left, share_right, unpainted
        """;

    public bool AnyRounds()
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM rounds") > 0;
    }

    /// <summary>The one round that isn't Done, if any.</summary>
    public RoundRecord? GetActive()
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<RoundRecord>($"SELECT {RoundColumns} FROM rounds WHERE status <> 'Done'");
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
        return c.ExecuteScalar<long>("""
            INSERT INTO rounds (channel_id, status, seed, generator_version, image_path, reveal_image_path,
                                created_at, reveal_at, left_name, left_color, left_emoji,
                                right_name, right_color, right_emoji, winner, winner_share,
                                share_left, share_right, unpainted)
            VALUES (@ChannelId, 'Pending', @Seed, @GeneratorVersion, @ImagePath, @RevealImagePath,
                    @CreatedAt, @RevealAt, @LeftName, @LeftColor, @LeftEmoji,
                    @RightName, @RightColor, @RightEmoji, @Winner, @WinnerShare,
                    @ShareLeft, @ShareRight, @Unpainted);
            SELECT last_insert_rowid();
            """, r);
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
    public IReadOnlyList<ulong> GetVoterIds(long roundId)
    {
        using var c = db.Open();
        return c.Query<long>(
            "SELECT user_id FROM votes WHERE round_id = @roundId ORDER BY user_name COLLATE NOCASE, user_id",
            new { roundId }).Select(id => (ulong)id).ToList();
    }

    public int CountVotes(long roundId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM votes WHERE round_id = @roundId", new { roundId });
    }
}
