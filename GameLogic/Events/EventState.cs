using System.Text.Json.Serialization;

namespace GameLogic.Events;

public sealed class EventSlot
{
    [JsonPropertyName("ID")] public int LocationId { get; set; }
    [JsonPropertyName("Status")] public int Status { get; set; } = 2;
    [JsonPropertyName("TimeStamp")] public long Timestamp { get; set; }
    [JsonPropertyName("Tokens")] public int Tokens { get; set; } = 10;
}

public sealed class EventState
{
    [JsonPropertyName("events")] public Dictionary<string, EventSlot> Slots { get; set; } = new();

    public IEnumerable<KeyValuePair<string, EventSlot>> Ordered() =>
        Slots.OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue);
}
