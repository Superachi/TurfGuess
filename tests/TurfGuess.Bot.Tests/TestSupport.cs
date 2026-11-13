using Microsoft.Extensions.Logging.Abstractions;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Tests;

/// <summary>A temp folder with a fresh database and image directory, deleted afterwards.</summary>
public sealed class TestEnvironment : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "turfguess-tests-" + Guid.NewGuid().ToString("N"));
    public BotOptions Options { get; }
    public Database Database { get; }
    public RoundRepository Repo { get; }
    public ScoreRepository Scores { get; }
    public FakeChannel Channel { get; } = new();
    public FakeNotifier Notifier { get; } = new();
    public SubscriptionRepository Subscriptions { get; }
    public FixedSeeds Seeds { get; } = new();
    public RoundImages Images { get; }
    public RoundService Rounds { get; }
    public VoteService Votes { get; }
    public ListLogger Log { get; } = new();

    public TestEnvironment()
    {
        Directory.CreateDirectory(Folder);
        Options = new BotOptions
        {
            Discord = { Token = "test", ChannelId = 1234 },
            Database = { Path = Path.Combine(Folder, "test.db") },
            Images = { Directory = Path.Combine(Folder, "images") },
            Game = { RoundTime = "09:00", TimeZone = "Europe/Amsterdam", MinRoundLength = TimeSpan.FromHours(6) },
            Generator = { Paint = { PixelsPerCell = 4 } }, // small images keep the tests fast
        };
        Database = new Database(Options.Database.Path);
        Database.EnsureSchema();
        Repo = new RoundRepository(Database);
        Scores = new ScoreRepository(Database);
        Images = new RoundImages(Options, NullLogger<RoundImages>.Instance);
        Subscriptions = new SubscriptionRepository(Database);
        Rounds = new RoundService(Repo, Scores, Subscriptions, Notifier, Channel, Images, Options, Seeds, Log.For<RoundService>());
        Votes = new VoteService(Repo, Scores, Options, TimeProvider.System, Log.For<VoteService>());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Collects formatted log messages from every logger it hands out.</summary>
public sealed class ListLogger
{
    private readonly List<string> _messages = [];
    public IReadOnlyList<string> Messages { get { lock (_messages) return _messages.ToList(); } }

    public Microsoft.Extensions.Logging.ILogger<T> For<T>() => new Logger<T>(this);

    private void Add(string message) { lock (_messages) _messages.Add(message); }

    private sealed class Logger<T>(ListLogger owner) : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Add(formatter(state, exception));
    }
}

public sealed class FixedSeeds : ISeedSource
{
    private ulong _next = 1000;
    public ulong NextSeed() => _next++;
}

/// <summary>Records direct messages. Users in <see cref="Unreachable"/> fail like a player with DMs closed.</summary>
public sealed class FakeNotifier : IPlayerNotifier
{
    public List<(ulong UserId, string Text)> Sent { get; } = [];
    public HashSet<ulong> Unreachable { get; } = [];

    /// <summary>The cards as sent, by user, for tests that look at the colour or link.</summary>
    public List<(ulong UserId, DirectMessage Message)> Cards { get; } = [];

    public Task SendDirectAsync(ulong userId, DirectMessage message, CancellationToken ct)
    {
        if (Unreachable.Contains(userId)) throw new InvalidOperationException("Cannot send messages to this user.");
        Cards.Add((userId, message));
        // Text is title, body, link and footer in reading order, so tests can check what the player sees.
        var parts = new[] { message.Title, message.Description, message.LinkedMessageId is { } id ? $"[{message.LinkLabel}](message {id})" : null, message.Footer };
        Sent.Add((userId, string.Join('\n', parts.Where(p => p is not null))));
        return Task.CompletedTask;
    }
}

/// <summary>An in-memory stand-in for the Discord channel that records what was posted.</summary>
public sealed class FakeChannel : IRoundChannel
{
    public sealed record Message(ulong Id, long RoundId, string Kind);

    private ulong _nextId = 900_000;
    public List<Message> Messages { get; } = [];
    public List<(long RoundId, RevealSummary Summary)> Reveals { get; } = [];
    public HashSet<long> ClosedRounds { get; } = [];
    public List<string> WeekSummaries { get; } = [];
    public List<(long RoundId, IReadOnlyList<Voter> Voters)> VoterUpdates { get; } = [];
    public Dictionary<long, IReadOnlyList<Voter>?> ClosedWithVoters { get; } = [];

    /// <summary>When set, the next post succeeds on "Discord" but then throws, like a crash right after sending.</summary>
    public bool CrashAfterNextPost { get; set; }

    public int RoundPosts => Messages.Count(m => m.Kind == "round");
    public int RevealPosts => Messages.Count(m => m.Kind == "reveal");

    public Task<ulong?> FindRoundMessageAsync(RoundRecord round, CancellationToken ct) =>
        Task.FromResult(Messages.FirstOrDefault(m => m.RoundId == round.Id && m.Kind == "round")?.Id);

    public List<(long RoundId, ulong ReplacedMessageId)> Replacements { get; } = [];

    public Task<ulong> PostRoundAsync(RoundRecord round, string imagePath, CancellationToken ct, ulong? replaceMessageId = null)
    {
        Assert.True(File.Exists(imagePath), "round image missing");
        if (replaceMessageId is { } id)
        {
            Replacements.Add((round.Id, id));
            Messages.Add(new Message(id, round.Id, "round"));
            return Task.FromResult(id);
        }
        return Post(round.Id, "round");
    }

    public Task ShowBattleRevealButtonAsync(ulong messageId, RoundRecord runningBattle, CancellationToken ct) => Task.CompletedTask;

    public Task CloseRoundMessageAsync(RoundRecord round, IReadOnlyList<Voter>? voters, CancellationToken ct)
    {
        ClosedRounds.Add(round.Id);
        ClosedWithVoters[round.Id] = voters;
        return Task.CompletedTask;
    }

    public Task UpdateVotersAsync(RoundRecord round, IReadOnlyList<Voter> voters, CancellationToken ct)
    {
        VoterUpdates.Add((round.Id, voters));
        return Task.CompletedTask;
    }

    public Task<ulong?> FindRevealMessageAsync(RoundRecord round, CancellationToken ct) =>
        Task.FromResult(Messages.FirstOrDefault(m => m.RoundId == round.Id && m.Kind == "reveal")?.Id);

    public HashSet<long> CancelledRounds { get; } = [];

    public Task CancelRoundMessageAsync(RoundRecord round, CancellationToken ct)
    {
        CancelledRounds.Add(round.Id);
        return Task.CompletedTask;
    }

    public Task<ulong> PostRevealAsync(RoundRecord round, RevealSummary summary, string imagePath, CancellationToken ct)
    {
        Assert.True(File.Exists(imagePath), "reveal image missing");
        Reveals.Add((round.Id, summary));
        return Post(round.Id, "reveal");
    }

    public Task PostWeekSummaryAsync(string text, CancellationToken ct)
    {
        WeekSummaries.Add(text);
        return Task.CompletedTask;
    }

    private Task<ulong> Post(long roundId, string kind)
    {
        var message = new Message(_nextId++, roundId, kind);
        Messages.Add(message);
        if (CrashAfterNextPost)
        {
            CrashAfterNextPost = false;
            throw new IOException("Simulated crash after posting.");
        }
        return Task.FromResult(message.Id);
    }
}
