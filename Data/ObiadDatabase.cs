using Microsoft.Data.Sqlite;

namespace Data;

public sealed class ObiadDatabase
{
    private readonly string _connectionString;

    public ObiadDatabase(string databasePath)
    {
        var full = Path.GetFullPath(databasePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void Migrate()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS players (
                token              TEXT PRIMARY KEY,
                low_id             INTEGER NOT NULL UNIQUE,
                name               TEXT    NOT NULL DEFAULT 'Brawler',
                club_id            INTEGER NOT NULL DEFAULT 0,
                club_role          INTEGER NOT NULL DEFAULT 0,
                team_id            INTEGER NOT NULL DEFAULT 0,
                experience         INTEGER NOT NULL DEFAULT 0,
                trophies           INTEGER NOT NULL DEFAULT 0,
                highest_trophies   INTEGER NOT NULL DEFAULT 0,
                trophy_rank        INTEGER NOT NULL DEFAULT 1,
                trophies_reward    INTEGER NOT NULL DEFAULT 0,
                solo_wins          INTEGER NOT NULL DEFAULT 0,
                duo_wins           INTEGER NOT NULL DEFAULT 0,
                trio_wins          INTEGER NOT NULL DEFAULT 0,
                gems               INTEGER NOT NULL DEFAULT 0,
                gold               INTEGER NOT NULL DEFAULT 0,
                tickets            INTEGER NOT NULL DEFAULT 0,
                upgrade_tokens     INTEGER NOT NULL DEFAULT 0,
                coins_reward       INTEGER NOT NULL DEFAULT 0,
                coins_doubler      INTEGER NOT NULL DEFAULT 0,
                coins_booster      INTEGER NOT NULL DEFAULT 0,
                profile_icon       INTEGER NOT NULL DEFAULT 0,
                tutorial_state     INTEGER NOT NULL DEFAULT 0,
                region             TEXT    NOT NULL DEFAULT 'EN',
                control_mode       INTEGER NOT NULL DEFAULT 0,
                has_battle_hints   INTEGER NOT NULL DEFAULT 0,
                player_status      INTEGER NOT NULL DEFAULT 0,
                last_connection    INTEGER NOT NULL DEFAULT 0,
                battle_id          INTEGER NOT NULL DEFAULT 0,
                banned             INTEGER NOT NULL DEFAULT 0,
                best_time_boss     INTEGER NOT NULL DEFAULT 0,
                best_time_survival INTEGER NOT NULL DEFAULT 0,
                brawlers           TEXT    NOT NULL DEFAULT '{}',
                home_notifications TEXT    NOT NULL DEFAULT '{}',
                player_upgrades    TEXT    NOT NULL DEFAULT '[]',
                friends            TEXT    NOT NULL DEFAULT '{}'
            );
            CREATE INDEX IF NOT EXISTS ix_players_trophies ON players(trophies DESC);
            CREATE INDEX IF NOT EXISTS ix_players_club     ON players(club_id);

            CREATE TABLE IF NOT EXISTS clubs (
                club_id     INTEGER PRIMARY KEY,
                name        TEXT NOT NULL DEFAULT '',
                trophies    INTEGER NOT NULL DEFAULT 0,
                data        TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS club_chats (
                club_id INTEGER PRIMARY KEY,
                data    TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS events (
                state INTEGER PRIMARY KEY,
                data  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS battles (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                data TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS bans (
                kind    TEXT NOT NULL,   -- 'account' | 'ip'
                subject TEXT NOT NULL,
                reason  TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (kind, subject)
            );
            """;
        cmd.ExecuteNonQuery();

        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT OR IGNORE INTO events (state, data) VALUES (1, '{"events":{}}');
            INSERT OR IGNORE INTO events (state, data) VALUES (2, '{"events":{}}');
            """;
        seed.ExecuteNonQuery();
    }
}
