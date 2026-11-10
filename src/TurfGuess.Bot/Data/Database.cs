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

        -- Every row in this partial index has the same key value, so at most one round is unfinished.
        CREATE UNIQUE INDEX IF NOT EXISTS one_active_round ON rounds (status <> 'Done') WHERE status <> 'Done';

        CREATE TABLE IF NOT EXISTS votes (
            round_id  INTEGER NOT NULL REFERENCES rounds (id),
            user_id   INTEGER NOT NULL,
            user_name TEXT    NOT NULL DEFAULT '',
            choice    TEXT    NOT NULL CHECK (choice IN ('left', 'right')),
            voted_at  INTEGER NOT NULL,
            PRIMARY KEY (round_id, user_id)
        );
        """;
}
