using System.Text.Json;
using Microsoft.Data.Sqlite;
using Data.Models;

namespace Data.Repositories;

public sealed class PlayerRepository(ObiadDatabase database)
{
    private const string Columns =
        "token, low_id, name, club_id, club_role, team_id, experience, trophies, highest_trophies, " +
        "trophy_rank, trophies_reward, solo_wins, duo_wins, trio_wins, gems, gold, tickets, " +
        "upgrade_tokens, coins_reward, coins_doubler, coins_booster, profile_icon, tutorial_state, " +
        "region, control_mode, has_battle_hints, player_status, last_connection, battle_id, banned, " +
        "best_time_boss, best_time_survival, brawlers, home_notifications, player_upgrades, friends";

    private const string Values =
        "$token, $low_id, $name, $club_id, $club_role, $team_id, $experience, $trophies, $highest_trophies, " +
        "$trophy_rank, $trophies_reward, $solo_wins, $duo_wins, $trio_wins, $gems, $gold, $tickets, " +
        "$upgrade_tokens, $coins_reward, $coins_doubler, $coins_booster, $profile_icon, $tutorial_state, " +
        "$region, $control_mode, $has_battle_hints, $player_status, $last_connection, $battle_id, $banned, " +
        "$best_time_boss, $best_time_survival, $brawlers, $home_notifications, $player_upgrades, $friends";

    private const string UpdateSet =
        "name=$name, club_id=$club_id, club_role=$club_role, team_id=$team_id, experience=$experience, " +
        "trophies=$trophies, highest_trophies=$highest_trophies, trophy_rank=$trophy_rank, " +
        "trophies_reward=$trophies_reward, solo_wins=$solo_wins, duo_wins=$duo_wins, trio_wins=$trio_wins, " +
        "gems=$gems, gold=$gold, tickets=$tickets, upgrade_tokens=$upgrade_tokens, " +
        "coins_reward=$coins_reward, coins_doubler=$coins_doubler, coins_booster=$coins_booster, " +
        "profile_icon=$profile_icon, tutorial_state=$tutorial_state, region=$region, " +
        "control_mode=$control_mode, has_battle_hints=$has_battle_hints, player_status=$player_status, " +
        "last_connection=$last_connection, battle_id=$battle_id, banned=$banned, " +
        "best_time_boss=$best_time_boss, best_time_survival=$best_time_survival, brawlers=$brawlers, " +
        "home_notifications=$home_notifications, player_upgrades=$player_upgrades, friends=$friends";

    public PlayerState? FindByToken(string token)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM players WHERE token = $t";
        cmd.Parameters.AddWithValue("$t", token);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool Exists(string token)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM players WHERE token = $t";
        cmd.Parameters.AddWithValue("$t", token);
        return cmd.ExecuteScalar() is not null;
    }

    public int NextLowId()
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(low_id), 0) + 1 FROM players";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Insert(PlayerState player)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO players (" + Columns + ") VALUES (" + Values + ")";
        Bind(cmd, player);
        cmd.ExecuteNonQuery();
    }

    public void Save(PlayerState player)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE players SET " + UpdateSet + " WHERE token=$token";
        Bind(cmd, player);
        cmd.ExecuteNonQuery();
    }

    public List<(int LowId, string Name, int Trophies)> TopByTrophies(int limit)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT low_id, name, trophies FROM players ORDER BY trophies DESC, low_id ASC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        using var reader = cmd.ExecuteReader();
        var result = new List<(int, string, int)>();
        while (reader.Read()) result.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        return result;
    }

    public PlayerState? FindByLowId(int lowId)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM players WHERE low_id = $id";
        cmd.Parameters.AddWithValue("$id", lowId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public sealed record LeaderboardEntry(int LowId, string Name, int Trophies, int Experience, int ProfileIcon);

    public List<LeaderboardEntry> TopByTrophiesDetailed(int limit)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT low_id, name, trophies, experience, profile_icon FROM players " +
            "ORDER BY trophies DESC, low_id ASC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        using var reader = cmd.ExecuteReader();
        var result = new List<LeaderboardEntry>();
        while (reader.Read())
            result.Add(new LeaderboardEntry(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        return result;
    }

    public List<LeaderboardEntry> TopByBrawlerTrophies(int brawlerId, int limit)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT low_id, name, " +
            "COALESCE(json_extract(brawlers, '$.\"' || $b || '\".Trophies'), -1) AS bt, " +
            "experience, profile_icon FROM players " +
            "WHERE bt >= 0 ORDER BY bt DESC, low_id ASC LIMIT $n";
        cmd.Parameters.AddWithValue("$b", brawlerId.ToString());
        cmd.Parameters.AddWithValue("$n", limit);
        using var reader = cmd.ExecuteReader();
        var result = new List<LeaderboardEntry>();
        while (reader.Read())
            result.Add(new LeaderboardEntry(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        return result;
    }

    public int Count()
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM players";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Bind(SqliteCommand cmd, PlayerState p)
    {
        cmd.Parameters.AddWithValue("$token", p.Token);
        cmd.Parameters.AddWithValue("$low_id", p.LowId);
        cmd.Parameters.AddWithValue("$name", p.Name);
        cmd.Parameters.AddWithValue("$club_id", p.ClubId);
        cmd.Parameters.AddWithValue("$club_role", p.ClubRole);
        cmd.Parameters.AddWithValue("$team_id", p.TeamId);
        cmd.Parameters.AddWithValue("$experience", p.Experience);
        cmd.Parameters.AddWithValue("$trophies", p.Trophies);
        cmd.Parameters.AddWithValue("$highest_trophies", p.HighestTrophies);
        cmd.Parameters.AddWithValue("$trophy_rank", p.TrophyRank);
        cmd.Parameters.AddWithValue("$trophies_reward", p.TrophiesReward);
        cmd.Parameters.AddWithValue("$solo_wins", p.SoloWins);
        cmd.Parameters.AddWithValue("$duo_wins", p.DuoWins);
        cmd.Parameters.AddWithValue("$trio_wins", p.TrioWins);
        cmd.Parameters.AddWithValue("$gems", p.Gems);
        cmd.Parameters.AddWithValue("$gold", p.Gold);
        cmd.Parameters.AddWithValue("$tickets", p.Tickets);
        cmd.Parameters.AddWithValue("$upgrade_tokens", p.UpgradeTokens);
        cmd.Parameters.AddWithValue("$coins_reward", p.CoinsReward);
        cmd.Parameters.AddWithValue("$coins_doubler", p.CoinsDoubler);
        cmd.Parameters.AddWithValue("$coins_booster", p.CoinsBooster);
        cmd.Parameters.AddWithValue("$profile_icon", p.ProfileIcon);
        cmd.Parameters.AddWithValue("$tutorial_state", p.TutorialState);
        cmd.Parameters.AddWithValue("$region", p.Region);
        cmd.Parameters.AddWithValue("$control_mode", p.ControlMode);
        cmd.Parameters.AddWithValue("$has_battle_hints", p.HasBattleHints ? 1 : 0);
        cmd.Parameters.AddWithValue("$player_status", p.PlayerStatus);
        cmd.Parameters.AddWithValue("$last_connection", p.LastConnectionTime);
        cmd.Parameters.AddWithValue("$battle_id", p.BattleId);
        cmd.Parameters.AddWithValue("$banned", p.Banned ? 1 : 0);
        cmd.Parameters.AddWithValue("$best_time_boss", p.BestTimeBoss);
        cmd.Parameters.AddWithValue("$best_time_survival", p.BestTimeSurvival);
        cmd.Parameters.AddWithValue("$brawlers", JsonSerializer.Serialize(p.Brawlers));
        cmd.Parameters.AddWithValue("$home_notifications", JsonSerializer.Serialize(p.HomeNotifications));
        cmd.Parameters.AddWithValue("$player_upgrades", JsonSerializer.Serialize(p.PlayerUpgrades));
        cmd.Parameters.AddWithValue("$friends", JsonSerializer.Serialize(p.Friends));
    }

    private static PlayerState Map(SqliteDataReader r) => new()
    {
        Token = r.GetString(0),
        LowId = r.GetInt32(1),
        Name = r.GetString(2),
        ClubId = r.GetInt32(3),
        ClubRole = r.GetInt32(4),
        TeamId = r.GetInt32(5),
        Experience = r.GetInt32(6),
        Trophies = r.GetInt32(7),
        HighestTrophies = r.GetInt32(8),
        TrophyRank = r.GetInt32(9),
        TrophiesReward = r.GetInt32(10),
        SoloWins = r.GetInt32(11),
        DuoWins = r.GetInt32(12),
        TrioWins = r.GetInt32(13),
        Gems = r.GetInt32(14),
        Gold = r.GetInt32(15),
        Tickets = r.GetInt32(16),
        UpgradeTokens = r.GetInt32(17),
        CoinsReward = r.GetInt32(18),
        CoinsDoubler = r.GetInt32(19),
        CoinsBooster = r.GetInt32(20),
        ProfileIcon = r.GetInt32(21),
        TutorialState = r.GetInt32(22),
        Region = r.GetString(23),
        ControlMode = r.GetInt32(24),
        HasBattleHints = r.GetInt32(25) != 0,
        PlayerStatus = r.GetInt32(26),
        LastConnectionTime = r.GetInt64(27),
        BattleId = r.GetInt32(28),
        Banned = r.GetInt32(29) != 0,
        BestTimeBoss = r.GetInt32(30),
        BestTimeSurvival = r.GetInt32(31),
        Brawlers = JsonSerializer.Deserialize<Dictionary<string, BrawlerState>>(r.GetString(32)) ?? new(),
        HomeNotifications = JsonSerializer.Deserialize<Dictionary<string, object>>(r.GetString(33)) ?? new(),
        PlayerUpgrades = JsonSerializer.Deserialize<List<int>>(r.GetString(34)) ?? [],
        Friends = JsonSerializer.Deserialize<Dictionary<string, object>>(r.GetString(35)) ?? new(),
    };
}
