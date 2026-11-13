using Dapper;

namespace TurfGuess.Bot.Data;

/// <summary>
/// Result messages are opt-out: every player who voted in a round gets a direct message with their result,
/// except those who asked to stop. This stores the opt-outs; "subscribed" simply means "not opted out".
/// </summary>
public sealed class SubscriptionRepository(Database db)
{
    /// <summary>Subscribes the player again. Returns false when they already were (never opted out).</summary>
    public bool Subscribe(long userId)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM subscription_opt_outs WHERE user_id = @userId", new { userId }) > 0;
    }

    /// <summary>Opts the player out. Returns false when they already had.</summary>
    public bool Unsubscribe(long userId, long now)
    {
        using var c = db.Open();
        return c.Execute("INSERT OR IGNORE INTO subscription_opt_outs (user_id, opted_out_at) VALUES (@userId, @now)", new { userId, now }) > 0;
    }

    public bool IsSubscribed(long userId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM subscription_opt_outs WHERE user_id = @userId", new { userId }) == 0;
    }

    /// <summary>Which of the given players are subscribed, i.e. have not opted out.</summary>
    public IReadOnlySet<long> SubscribedAmong(IReadOnlyCollection<long> userIds)
    {
        if (userIds.Count == 0) return new HashSet<long>();
        using var c = db.Open();
        var optedOut = c.Query<long>("SELECT user_id FROM subscription_opt_outs WHERE user_id IN @userIds", new { userIds }).ToHashSet();
        return userIds.Where(id => !optedOut.Contains(id)).ToHashSet();
    }
}
