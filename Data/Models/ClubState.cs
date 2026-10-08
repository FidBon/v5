using System.Text.Json.Serialization;

namespace Data.Models;

public static class ClubType
{
    public const int Open = 1;
    public const int InviteOnly = 2;
    public const int Closed = 3;
}

public static class ClubRole
{
    public const int Member = 1;
    public const int President = 2;
    public const int Elder = 3;
    public const int VicePresident = 4;
}

public sealed class ClubState
{
    public int ClubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Region { get; set; } = "EN";
    public int Badge { get; set; }
    public int Type { get; set; } = ClubType.Open;
    public int RequiredTrophies { get; set; }

    public List<string> Members { get; set; } = [];
}

public sealed class ClubStreamEntry
{
    [JsonPropertyName("eventType")] public int EventType { get; set; }
    [JsonPropertyName("event")] public int Event { get; set; }
    [JsonPropertyName("tick")] public int Tick { get; set; }
    [JsonPropertyName("playerId")] public int PlayerId { get; set; }
    [JsonPropertyName("playerName")] public string PlayerName { get; set; } = string.Empty;
    [JsonPropertyName("playerRole")] public int PlayerRole { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    [JsonPropertyName("targetId")] public int TargetId { get; set; }
    [JsonPropertyName("targetName")] public string TargetName { get; set; } = string.Empty;

    public const int TypeChat = 2;
    public const int TypeAllianceEvent = 4;

    public const int EventJoined = 3;
    public const int EventLeft = 4;
}

public sealed class ClubChat
{
    public List<ClubStreamEntry> Messages { get; set; } = [];

    public const int MaxMessages = 60;
}
