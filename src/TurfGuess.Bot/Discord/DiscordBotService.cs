using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Owns the Discord connection: logs in, registers the button handlers and slash commands for the
/// game's server, routes interactions, and signals the scheduler once connected.
/// </summary>
public sealed class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    BotOptions options,
    ChannelReadySignal ready,
    ILogger<DiscordBotService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += LogAsync;
        interactions.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionAsync;

        await interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, services);
        await client.LoginAsync(TokenType.Bot, options.Discord.Token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private Task OnReadyAsync()
    {
        // Don't block the gateway thread; registering commands is a REST call.
        _ = Task.Run(async () =>
        {
            try
            {
                var channel = client.GetChannel(options.Discord.ChannelId) as IGuildChannel
                              ?? await client.Rest.GetChannelAsync(options.Discord.ChannelId) as IGuildChannel;
                if (channel is null)
                {
                    logger.LogCritical("Channel {ChannelId} not found, or it isn't a server text channel the bot can see.",
                        options.Discord.ChannelId);
                    return;
                }

                // Guild commands show up instantly (global ones can take up to an hour).
                await interactions.RegisterCommandsToGuildAsync(channel.GuildId);
                logger.LogInformation("Connected as {User}; playing in #{Channel}.", client.CurrentUser, channel.Name);
                ready.SetReady();
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Setting up after connecting to Discord failed.");
            }
        });
        return Task.CompletedTask;
    }

    private async Task OnInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(client, interaction);
            var result = await interactions.ExecuteCommandAsync(context, services);
            if (!result.IsSuccess)
            {
                logger.LogWarning("Interaction failed: {Error} {Reason}", result.Error, result.ErrorReason);
                if (!interaction.HasResponded)
                    await interaction.RespondAsync("Something went wrong. Please try again.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handling an interaction failed.");
        }
    }

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        logger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
