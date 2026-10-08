using System.Text.Json;

namespace Data.Repositories;

public sealed class EventRepository(ObiadDatabase database)
{
    public string LoadRaw(int state)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM events WHERE state = $s";
        cmd.Parameters.AddWithValue("$s", state);
        return cmd.ExecuteScalar() as string ?? "{\"events\":{}}";
    }

    public T Load<T>(int state) where T : new() =>
        JsonSerializer.Deserialize<T>(LoadRaw(state)) ?? new T();

    public void Save<T>(int state, T value)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO events (state, data) VALUES ($s, $d)";
        cmd.Parameters.AddWithValue("$s", state);
        cmd.Parameters.AddWithValue("$d", JsonSerializer.Serialize(value));
        cmd.ExecuteNonQuery();
    }
}
