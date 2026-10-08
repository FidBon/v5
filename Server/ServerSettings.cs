using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server;

public sealed class ServerSettings
{
    [JsonPropertyName("Address")] public string Address { get; set; } = "0.0.0.0";
    [JsonPropertyName("Port")] public int Port { get; set; } = 8888;
    [JsonPropertyName("RC4Key")] public string Rc4Key { get; set; } = "fhsd6f86f67rt8fw78fw789we78r9789wer6re";
    [JsonPropertyName("usedCryptography")] public string Cryptography { get; set; } = "RC4";

    [JsonPropertyName("MaximumRank")] public int MaximumRank { get; set; } = 20;
    [JsonPropertyName("MaximumUpgradeLevel")] public int MaximumUpgradeLevel { get; set; } = 5;
    [JsonPropertyName("startingTutorialState")] public int StartingTutorialState { get; set; } = 2;
    [JsonPropertyName("seasonEndBonus")] public int SeasonEndBonus { get; set; }
    [JsonPropertyName("nextSeasonEndTimestamp")] public long NextSeasonEndTimestamp { get; set; }
    [JsonPropertyName("ticketsPrice")] public int TicketsPrice { get; set; } = 1;

    [JsonPropertyName("gamePatcher")] public bool PatcherEnabled { get; set; } = true;
    [JsonPropertyName("gamePatcherAddress")] public string PatcherAddress { get; set; } = "0.0.0.0";
    [JsonPropertyName("gamePatcherPort")] public int PatcherPort { get; set; } = 9999;
    [JsonPropertyName("gamePatcherUrl")] public string PatcherUrl { get; set; } = string.Empty;
    [JsonPropertyName("gamePatcherContent")] public string PatcherContent { get; set; } = "Patcher/Content";
    [JsonPropertyName("gamePatcherFingerprint")] public string PatcherFingerprint { get; set; } = "Patcher/base-fingerprint.json";

    [JsonPropertyName("UseUDPServer")] public bool BattleServerEnabled { get; set; }
    [JsonPropertyName("UDPAddress")] public string BattleAddress { get; set; } = "0.0.0.0";
    [JsonPropertyName("UDPPort")] public int BattlePort { get; set; } = 6666;

    [JsonPropertyName("playersPerMatch")] public int PlayersPerMatch { get; set; } = 6;
    [JsonPropertyName("matchmakingFillWithBotsSeconds")] public int MatchmakingFillWithBotsSeconds { get; set; } = 12;
    [JsonPropertyName("battleMaxTicks")] public int BattleMaxTicks { get; set; } = 16000;
    [JsonPropertyName("eventText")] public string EventText { get; set; } = "67";

    [JsonPropertyName("showdownBoxes")] public bool ShowdownBoxes { get; set; } = true;

    [JsonPropertyName("battleLocationId")] public int BattleLocationId { get; set; }

    [JsonPropertyName("databasePath")] public string DatabasePath { get; set; } = "Database/obiad.db";
    [JsonPropertyName("assetsRoot")] public string AssetsRoot { get; set; } = "Assets";

    public static ServerSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new ServerSettings();
            fresh.Save(path);
            Console.WriteLine($"[config] {path} не найден, создан со значениями по умолчанию");
            return fresh;
        }

        var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var settings = JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path), options);
        return settings ?? new ServerSettings();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
