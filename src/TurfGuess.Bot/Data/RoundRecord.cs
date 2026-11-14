using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Data;

/// <summary>Lifecycle of a round (DESIGN.md §2, Round lifecycle).</summary>
public enum RoundStatus
{
    /// <summary>Row written; the message may or may not be posted yet.</summary>
    Pending,

    /// <summary>Round message posted; votes are accepted.</summary>
    Open,

    /// <summary>Voting closed, votes final; the reveal may or may not be posted yet.</summary>
    Revealed,

    /// <summary>Reveal posted.</summary>
    Done,
}

/// <summary>Which game a round belongs to. Each kind has at most one unfinished round at a time.</summary>
public enum RoundKind
{
    /// <summary>⚓ The daily round. Scored, counts towards the weekly standings.</summary>
    Ranked,

    /// <summary>🔫 Started by a player with a command. Short, not scored, never touches the weekly standings.</summary>
    Regular,
}

/// <summary>One row of the <c>rounds</c> table. Discord ids are stored as INTEGER (signed 64-bit).</summary>
public sealed class RoundRecord
{
    public long Id { get; set; }

    /// <summary>The count shown to players, kept separately for each <see cref="Kind"/> (0 = not assigned yet).</summary>
    public long Number { get; set; }

    /// <summary>The number players see: the per-kind count, or the id when none is stored.</summary>
    public long DisplayNumber => Number > 0 ? Number : Id;
    public long ChannelId { get; set; }
    public RoundStatus Status { get; set; }
    public RoundKind Kind { get; set; } = RoundKind.Ranked;

    /// <summary>Regular battles only: the round message lists who voted for which team while voting is open.</summary>
    public bool ShowVotes { get; set; }

    /// <summary>Regular battles only: the player who started it (0 for ranked rounds).</summary>
    public long StartedBy { get; set; }

    /// <summary>Ranked rounds only: the "time is running out" reminder has been handled (posted or deliberately skipped).</summary>
    public bool ReminderSent { get; set; }
    public long? MessageId { get; set; }
    public long? RevealMessageId { get; set; }

    public long Seed { get; set; }
    public int GeneratorVersion { get; set; }
    public string ImagePath { get; set; } = "";
    public string RevealImagePath { get; set; } = "";

    /// <summary>Unix seconds (UTC).</summary>
    public long CreatedAt { get; set; }
    public long? PostedAt { get; set; }
    public long RevealAt { get; set; }

    public string LeftName { get; set; } = "";
    public string LeftColor { get; set; } = "";
    public string LeftEmoji { get; set; } = "";
    public string RightName { get; set; } = "";
    public string RightColor { get; set; } = "";
    public string RightEmoji { get; set; } = "";

    /// <summary>'left' or 'right'.</summary>
    public string Winner { get; set; } = "";

    /// <summary>Share of PAINTED floor, 0–1 (difficulty).</summary>
    public double WinnerShare { get; set; }

    /// <summary>Share of TOTAL floor, 0–1 (shown at reveal).</summary>
    public double ShareLeft { get; set; }
    public double ShareRight { get; set; }
    public double Unpainted { get; set; }

    public Side WinnerSide => SideCodec.Parse(Winner);
    public DateTimeOffset RevealAtTime => DateTimeOffset.FromUnixTimeSeconds(RevealAt);
    public DateTimeOffset CreatedAtTime => DateTimeOffset.FromUnixTimeSeconds(CreatedAt);

    public string TeamName(Side side) => side == Side.Left ? LeftName : RightName;
    public string TeamEmoji(Side side) => side == Side.Left ? LeftEmoji : RightEmoji;
    public double Coverage(Side side) => side == Side.Left ? ShareLeft : ShareRight;
}

/// <summary>One row of the <c>votes</c> table.</summary>
public sealed class VoteRecord
{
    public long RoundId { get; set; }
    public long UserId { get; set; }

    /// <summary>Discord username at the time of the latest vote, for logs.</summary>
    public string UserName { get; set; } = "";

    public string Choice { get; set; } = "";
    public long VotedAt { get; set; }

    public Side ChoiceSide => SideCodec.Parse(Choice);
}

/// <summary>How a <see cref="Side"/> is written in the database and in button ids.</summary>
public static class SideCodec
{
    public static string Format(Side side) => side == Side.Left ? "left" : "right";

    public static Side Parse(string value) => value switch
    {
        "left" => Side.Left,
        "right" => Side.Right,
        _ => throw new FormatException($"'{value}' is not a side."),
    };

    public static bool TryParse(string value, out Side side)
    {
        side = Side.Left;
        if (value == "left") return true;
        side = Side.Right;
        return value == "right";
    }
}
