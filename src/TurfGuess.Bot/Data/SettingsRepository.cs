using Dapper;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Data;

/// <summary>
/// Settings an admin can change while the bot runs. They are stored in the database and win over appsettings.json.
/// </summary>
public sealed class SettingsRepository(Database db, BotOptions options)
{
    private const string RankedChannelKey = "ranked_channel_id";

    /// <summary>The channel ranked battles are posted in: the admin's choice, else <c>Discord:ChannelId</c>.</summary>
    public ulong RankedChannelId()
    {
        using var c = db.Open();
        var stored = c.ExecuteScalar<string?>("SELECT value FROM settings WHERE key = @key", new { key = RankedChannelKey });
        return ulong.TryParse(stored, out var id) && id != 0 ? id : options.Discord.ChannelId;
    }

    public void SetRankedChannelId(ulong channelId)
    {
        using var c = db.Open();
        c.Execute("INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            new { key = RankedChannelKey, value = channelId.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
}
