using Dapper;

namespace TurfGuess.Bot.Data;

/// <summary>Players who asked for a direct message with their result after each round they voted in.</summary>
public sealed class SubscriptionRepository(Database db)
{
    /// <summary>Subscribes the player. Returns false when they already were.</summary>
    public bool Subscribe(long userId, long now)
    {
        using var c = db.Open();
        return c.Execute("INSERT OR IGNORE INTO subscriptions (user_id, subscribed_at) VALUES (@userId, @now)", new { userId, now }) > 0;
    }

    /// <summary>Unsubscribes the player. Returns false when they weren't subscribed.</summary>
    public bool Unsubscribe(long userId)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM subscriptions WHERE user_id = @userId", new { userId }) > 0;
    }

    public bool IsSubscribed(long userId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM subscriptions WHERE user_id = @userId", new { userId }) > 0;
    }

    /// <summary>Which of the given players are subscribed.</summary>
    public IReadOnlySet<long> SubscribedAmong(IReadOnlyCollection<long> userIds)
    {
        if (userIds.Count == 0) return new HashSet<long>();
        using var c = db.Open();
        return c.Query<long>("SELECT user_id FROM subscriptions WHERE user_id IN @userIds", new { userIds }).ToHashSet();
    }
}
