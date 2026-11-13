using Dapper;
using Microsoft.Data.Sqlite;

namespace TurfGuess.Bot.Data;

/// <summary>Opens SQLite connections and creates the schema (DESIGN.md §6).</summary>
public sealed class Database
{
    private readonly string _connectionString;

    static Database()
    {
        // Columns are snake_case, properties PascalCase.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public Database(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            ForeignKeys = true,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Creates tables and indexes if they don't exist yet. Safe to run on every start.</summary>
    public void EnsureSchema()
    {
        using var c = Open();
        c.Execute("PRAGMA journal_mode = WAL;");
        c.Execute(Schema);

        // Upgrades for databases created by older versions.
        AddColumnIfMissing(c, "votes", "user_name", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(c, "rounds", "kind", "TEXT NOT NULL DEFAULT 'Ranked' CHECK (kind IN ('Ranked', 'Regular'))");
        AddColumnIfMissing(c, "rounds", "show_votes", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "rounds", "started_by", "INTEGER NOT NULL DEFAULT 0");

        // At most one unfinished round per kind (the old index allowed one in total).
        c.Execute("DROP INDEX IF EXISTS one_active_round;");
        c.Execute("CREATE UNIQUE INDEX IF NOT EXISTS one_active_round_per_kind ON rounds (kind) WHERE status <> 'Done';");
    }

    private static void AddColumnIfMissing(SqliteConnection c, string table, string column, string definition)
    {
        bool exists = c.Query<string>($"SELECT name FROM pragma_table_info('{table}')").Contains(column);
        if (!exists) c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS rounds (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            channel_id        INTEGER NOT NULL,
            status            TEXT    NOT NULL CHECK (status IN ('Pending', 'Open', 'Revealed', 'Done')),
            message_id        INTEGER,
            reveal_message_id INTEGER,
            seed              INTEGER NOT NULL,
            generator_version INTEGER NOT NULL,
            image_path        TEXT    NOT NULL DEFAULT '',
            reveal_image_path TEXT    NOT NULL DEFAULT '',
            created_at        INTEGER NOT NULL,
            posted_at         INTEGER,
            reveal_at         INTEGER NOT NULL,
            left_name         TEXT    NOT NULL,
            left_color        TEXT    NOT NULL,
            left_emoji        TEXT    NOT NULL,
            right_name        TEXT    NOT NULL,
            right_color       TEXT    NOT NULL,
            right_emoji       TEXT    NOT NULL,
            winner            TEXT    NOT NULL CHECK (winner IN ('left', 'right')),
            winner_share      REAL    NOT NULL,
            share_left        REAL    NOT NULL,
            share_right       REAL    NOT NULL,
            unpainted         REAL    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS votes (
            round_id  INTEGER NOT NULL REFERENCES rounds (id),
            user_id   INTEGER NOT NULL,
            user_name TEXT    NOT NULL DEFAULT '',
            choice    TEXT    NOT NULL CHECK (choice IN ('left', 'right')),
            voted_at  INTEGER NOT NULL,
            PRIMARY KEY (round_id, user_id)
        );

        -- A row means the player wants to bet on this round. It only counts if they also voted.
        CREATE TABLE IF NOT EXISTS bets (
            round_id  INTEGER NOT NULL REFERENCES rounds (id),
            user_id   INTEGER NOT NULL,
            PRIMARY KEY (round_id, user_id)
        );

        -- One row per voter per revealed round, written once at the reveal. delta is the change
        -- actually applied to the weekly score (after the 0 floor). week_start is the Monday, yyyy-MM-dd.
        CREATE TABLE IF NOT EXISTS round_results (
            round_id   INTEGER NOT NULL REFERENCES rounds (id),
            user_id    INTEGER NOT NULL,
            week_start TEXT    NOT NULL,
            correct    INTEGER NOT NULL,
            bet        INTEGER NOT NULL,
            delta      INTEGER NOT NULL,
            PRIMARY KEY (round_id, user_id)
        );
        CREATE INDEX IF NOT EXISTS round_results_week ON round_results (week_start);

        -- Weeks whose summary is done, and who won them (the trophy tally).
        CREATE TABLE IF NOT EXISTS closed_weeks (
            week_start TEXT PRIMARY KEY
        );

        CREATE TABLE IF NOT EXISTS weekly_winners (
            week_start TEXT    NOT NULL,
            user_id    INTEGER NOT NULL,
            points     INTEGER NOT NULL,
            wins       INTEGER NOT NULL,
            PRIMARY KEY (week_start, user_id)
        );
        """;
}
