using System.Text.Json;
using Data.Models;

namespace Data.Repositories;

public sealed class ClubRepository(ObiadDatabase database)
{
    public ClubState? Find(int clubId)
    {
        if (clubId == 0) return null;
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM clubs WHERE club_id = $id";
        cmd.Parameters.AddWithValue("$id", clubId);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<ClubState>(json) : null;
    }

    public int NextClubId()
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(club_id), 0) + 1 FROM clubs";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Save(ClubState club, int trophies)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT OR REPLACE INTO clubs (club_id, name, trophies, data) VALUES ($id, $name, $tr, $data)";
        cmd.Parameters.AddWithValue("$id", club.ClubId);
        cmd.Parameters.AddWithValue("$name", club.Name);
        cmd.Parameters.AddWithValue("$tr", trophies);
        cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(club));
        cmd.ExecuteNonQuery();
    }

    public void Delete(int clubId)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM clubs WHERE club_id = $id; DELETE FROM club_chats WHERE club_id = $id;";
        cmd.Parameters.AddWithValue("$id", clubId);
        cmd.ExecuteNonQuery();
    }

    public List<ClubState> List(int limit, string? nameFilter = null)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = nameFilter is null
            ? "SELECT data FROM clubs ORDER BY trophies DESC, club_id ASC LIMIT $n"
            : "SELECT data FROM clubs WHERE lower(name) LIKE $f ORDER BY trophies DESC, club_id ASC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        if (nameFilter is not null) cmd.Parameters.AddWithValue("$f", "%" + nameFilter.ToLowerInvariant() + "%");

        using var reader = cmd.ExecuteReader();
        var result = new List<ClubState>();
        while (reader.Read())
        {
            var club = JsonSerializer.Deserialize<ClubState>(reader.GetString(0));
            if (club is not null) result.Add(club);
        }
        return result;
    }

    public ClubChat LoadChat(int clubId)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM club_chats WHERE club_id = $id";
        cmd.Parameters.AddWithValue("$id", clubId);
        return cmd.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<ClubChat>(json) ?? new ClubChat()
            : new ClubChat();
    }

    public void SaveChat(int clubId, ClubChat chat)
    {
        if (chat.Messages.Count > ClubChat.MaxMessages)
            chat.Messages = chat.Messages.TakeLast(ClubChat.MaxMessages).ToList();

        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO club_chats (club_id, data) VALUES ($id, $data)";
        cmd.Parameters.AddWithValue("$id", clubId);
        cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(chat));
        cmd.ExecuteNonQuery();
    }

    public void Append(int clubId, ClubStreamEntry entry)
    {
        var chat = LoadChat(clubId);
        entry.Tick = chat.Messages.Count + 1;
        chat.Messages.Add(entry);
        SaveChat(clubId, chat);
    }
}
