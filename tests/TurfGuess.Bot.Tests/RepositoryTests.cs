using Microsoft.Data.Sqlite;
using TurfGuess.Bot.Data;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

public class RepositoryTests
{
    private static RoundRecord NewRound() => new()
    {
        ChannelId = 1, Seed = 5, GeneratorVersion = 1, CreatedAt = 100, RevealAt = 200,
        LeftName = "Pink", LeftColor = "#E0559A", LeftEmoji = "🩷",
        RightName = "Green", RightColor = "#2E7D32", RightEmoji = "💚",
        Winner = "left", WinnerShare = 0.52, ShareLeft = 0.47, ShareRight = 0.43, Unpainted = 0.10,
    };

    [Fact]
    public void Round_round_trips_through_the_database()
    {
        using var env = new TestEnvironment();
        long id = env.Repo.Insert(NewRound());
        var r = env.Repo.Get(id)!;

        Assert.Equal(RoundStatus.Pending, r.Status);
        Assert.Equal("Pink", r.LeftName);
        Assert.Equal("🩷", r.LeftEmoji);
        Assert.Equal(Side.Left, r.WinnerSide);
        Assert.Equal(0.52, r.WinnerShare, 9);
        Assert.Null(r.MessageId);
    }

    [Fact]
    public void Only_one_unfinished_round_can_exist()
    {
        using var env = new TestEnvironment();
        env.Repo.Insert(NewRound());
        var ex = Assert.Throws<SqliteException>(() => env.Repo.Insert(NewRound()));
        Assert.Contains("UNIQUE", ex.Message);
    }

    [Fact]
    public void Status_moves_forward_only_from_the_expected_state()
    {
        using var env = new TestEnvironment();
        long id = env.Repo.Insert(NewRound());

        env.Repo.MarkRevealed(id);                       // not Open yet: ignored
        Assert.Equal(RoundStatus.Pending, env.Repo.Get(id)!.Status);

        env.Repo.MarkOpen(id, 77, 150);
        env.Repo.MarkRevealed(id);
        env.Repo.MarkDone(id, 88);
        var r = env.Repo.Get(id)!;
        Assert.Equal(RoundStatus.Done, r.Status);
        Assert.Equal(77, r.MessageId);
        Assert.Equal(88, r.RevealMessageId);
        Assert.Null(env.Repo.GetActive());

        env.Repo.Insert(NewRound()); // allowed again once the previous one is Done
    }

    [Fact]
    public void Votes_are_only_accepted_while_open_and_can_change()
    {
        using var env = new TestEnvironment();
        long id = env.Repo.Insert(NewRound());

        Assert.False(env.Repo.TryUpsertVote(id, 1, Side.Left, 10)); // Pending

        env.Repo.MarkOpen(id, 77, 150);
        Assert.True(env.Repo.TryUpsertVote(id, 1, Side.Left, 11));
        Assert.True(env.Repo.TryUpsertVote(id, 1, Side.Right, 12)); // change
        Assert.True(env.Repo.TryUpsertVote(id, 2, Side.Left, 13));

        var votes = env.Repo.GetVotes(id);
        Assert.Equal(2, votes.Count);
        Assert.Equal(Side.Right, votes.Single(v => v.UserId == 1).ChoiceSide);
        Assert.Equal(2, env.Repo.CountVotes(id));

        env.Repo.MarkRevealed(id);
        Assert.False(env.Repo.TryUpsertVote(id, 3, Side.Left, 14)); // closed
        Assert.Equal(2, env.Repo.CountVotes(id));
    }

    [Fact]
    public void Large_discord_ids_survive_the_round_trip()
    {
        using var env = new TestEnvironment();
        long id = env.Repo.Insert(NewRound());
        ulong snowflake = 1_234_567_890_123_456_789;
        env.Repo.MarkOpen(id, (long)snowflake, 1);
        env.Repo.TryUpsertVote(id, (long)snowflake, Side.Left, 2);

        Assert.Equal(snowflake, (ulong)env.Repo.Get(id)!.MessageId!.Value);
        Assert.Equal(snowflake, (ulong)env.Repo.GetVotes(id).Single().UserId);
    }

    [Fact]
    public void Schema_creation_is_repeatable()
    {
        using var env = new TestEnvironment();
        env.Database.EnsureSchema();
        env.Database.EnsureSchema();
        Assert.False(env.Repo.AnyRounds());
    }
}
