using Discord.Interactions;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// <c>/turf subscribe</c> and <c>/turf unsubscribe</c>: a private message with the result after each round
/// the player voted in. Works in any channel, since the messages go to the player's DMs.
/// </summary>
[Group("turf", "Play the daily guessing game")]
public sealed class SubscribeModule(
    SubscriptionRepository subscriptions,
    IPlayerNotifier notifier,
    TimeProvider clock,
    ILogger<SubscribeModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("subscribe", "Get a private message with your result after every round you voted in.")]
    public async Task SubscribeAsync()
    {
        if (!subscriptions.Subscribe((long)Context.User.Id, clock.GetUtcNow().ToUnixTimeSeconds()))
        {
            await RespondAsync(MessageText.AlreadySubscribed, ephemeral: true);
            return;
        }

        logger.LogInformation("{UserName} ({UserId}) subscribed to result messages.", Context.User.Username, Context.User.Id);
        await DeferAsync(ephemeral: true);

        // Check right away that messages get through, so a closed DM isn't discovered after the next reveal.
        string reply = MessageText.SubscribeConfirmation;
        try { await notifier.SendDirectAsync(Context.User.Id, MessageText.SubscribeTestMessage, default); }
        catch (Exception ex)
        {
            logger.LogWarning("Couldn't send a test message to new subscriber {UserId}: {Reason}", Context.User.Id, ex.Message);
            reply = MessageText.SubscribeNoDms;
        }
        await FollowupAsync(reply, ephemeral: true);
    }

    [SlashCommand("unsubscribe", "Stop the private result messages.")]
    public async Task UnsubscribeAsync()
    {
        bool was = subscriptions.Unsubscribe((long)Context.User.Id);
        if (was) logger.LogInformation("{UserName} ({UserId}) unsubscribed from result messages.", Context.User.Username, Context.User.Id);
        await RespondAsync(was ? MessageText.Unsubscribed : MessageText.NotSubscribed, ephemeral: true);
    }
}
