using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Discord;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

// appsettings.json is read from the app's own folder, wherever it's started from. Relative
// Database:Path and Images:Directory resolve against the working directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// appsettings.json < user secrets (development) < TURFGUESS_* environment variables.
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);
builder.Configuration.AddEnvironmentVariables(prefix: "TURFGUESS_");

var options = builder.Configuration.Get<BotOptions>() ?? new BotOptions();

// --preview-messages [--seed N] [--raw]: print every message for a sample round and exit.
// No token or Discord connection needed.
if (args.Contains("--preview-messages"))
{
    int seedIndex = Array.IndexOf(args, "--seed");
    ulong seed = seedIndex >= 0 && seedIndex + 1 < args.Length && ulong.TryParse(args[seedIndex + 1], out var s) ? s : 42;
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(MessagePreview.Render(options, seed, DateTimeOffset.UtcNow, raw: args.Contains("--raw")));
    return 0;
}

var errors = BotOptionsValidator.Validate(options);
if (errors.Count > 0)
{
    Console.Error.WriteLine($"{options.Game.Name} can't start, the configuration has problems:");
    foreach (var e in errors) Console.Error.WriteLine($"  - {e}");
    return 1;
}

var services = builder.Services;
services.AddSingleton(options);
services.AddSingleton(TimeProvider.System);

// Data
var database = new Database(options.Database.Path);
database.EnsureSchema();
services.AddSingleton(database);
services.AddSingleton<RoundRepository>();

// Game
services.AddSingleton<ISeedSource, RandomSeedSource>();
services.AddSingleton<RoundImages>();
services.AddSingleton<VoteService>();
services.AddSingleton<RoundService>();
services.AddSingleton<SchedulerSignal>();
services.AddSingleton<ChannelReadySignal>();

// Discord
services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds, // no privileged intents needed
    LogLevel = LogSeverity.Info,
}));
services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>().Rest));
services.AddSingleton<IRoundChannel, DiscordRoundChannel>();

services.AddHostedService<DiscordBotService>();
services.AddHostedService<RoundScheduler>();
services.AddHostedService<VoteCountService>();

await builder.Build().RunAsync();
return 0;
