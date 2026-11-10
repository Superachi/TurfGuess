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
    public FakeChannel Channel { get; } = new();
    public FixedSeeds Seeds { get; } = new();
    public RoundImages Images { get; }
    public RoundService Rounds { get; }

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
        Images = new RoundImages(Options, NullLogger<RoundImages>.Instance);
        Rounds = new RoundService(Repo, Channel, Images, Options, Seeds, NullLogger<RoundService>.Instance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

public sealed class FixedSeeds : ISeedSource
{
    private ulong _next = 1000;
    public ulong NextSeed() => _next++;
}

/// <summary>An in-memory stand-in for the Discord channel that records what was posted.</summary>
public sealed class FakeChannel : IRoundChannel
{
    public sealed record Message(ulong Id, long RoundId, string Kind);

    private ulong _nextId = 900_000;
    public List<Message> Messages { get; } = [];
    public List<(long RoundId, RevealSummary Summary)> Reveals { get; } = [];
    public HashSet<long> ClosedRounds { get; } = [];
    public List<(long RoundId, int Voters)> VoteCountUpdates { get; } = [];

    /// <summary>When set, the next post succeeds on "Discord" but then throws, like a crash right after sending.</summary>
    public bool CrashAfterNextPost { get; set; }

    public int RoundPosts => Messages.Count(m => m.Kind == "round");
    public int RevealPosts => Messages.Count(m => m.Kind == "reveal");

    public Task<ulong?> FindRoundMessageAsync(long roundId, CancellationToken ct) =>
        Task.FromResult(Messages.FirstOrDefault(m => m.RoundId == roundId && m.Kind == "round")?.Id);

    public Task<ulong> PostRoundAsync(RoundRecord round, string imagePath, CancellationToken ct)
    {
        Assert.True(File.Exists(imagePath), "round image missing");
        return Post(round.Id, "round");
    }

    public Task CloseRoundMessageAsync(RoundRecord round, int? voters, CancellationToken ct)
    {
        ClosedRounds.Add(round.Id);
        return Task.CompletedTask;
    }

    public Task UpdateVoteCountAsync(RoundRecord round, int voters, CancellationToken ct)
    {
        VoteCountUpdates.Add((round.Id, voters));
        return Task.CompletedTask;
    }

    public Task<ulong?> FindRevealMessageAsync(long roundId, CancellationToken ct) =>
        Task.FromResult(Messages.FirstOrDefault(m => m.RoundId == roundId && m.Kind == "reveal")?.Id);

    public Task<ulong> PostRevealAsync(RoundRecord round, RevealSummary summary, string imagePath, CancellationToken ct)
    {
        Assert.True(File.Exists(imagePath), "reveal image missing");
        Reveals.Add((round.Id, summary));
        return Post(round.Id, "reveal");
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
