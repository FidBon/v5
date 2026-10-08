namespace GameLogic.Csv;

public sealed class Locations(CsvTable table, MapCatalog maps)
{
    private const string TutorialName = "Tutorial";
    private const int ColName = 0;
    private const int ColGameMode = 13;
    private const int ColAllowedMaps = 14;

    public CsvTable Table { get; } = table;

    public string GetName(int id) => Table.Cell(id, ColName);

    public string GetGameMode(int id)
    {
        var mode = Table.Cell(id, ColGameMode);
        return mode.Length == 0 ? "CoinRush" : mode;
    }

    public string GetAllowedMap(int id) => Table.Cell(id, ColAllowedMaps).Trim();

    public bool HasBattleMap(int id) => maps.Find(GetAllowedMap(id)) is not null;

    public bool IsBattleReady(int id) => HasBattleMap(id);

    public bool IsSelectable(int id) =>
        !Table.CellBool(id, "Disabled")
        && Table.Cell(id, ColName) != TutorialName
        && IsBattleReady(id);

    public List<int> WithGameModes(IReadOnlyCollection<string> gameModes)
    {
        var result = new List<int>();
        for (int i = 0; i < Table.Count; i++)
        {
            if (!IsSelectable(i)) continue;
            if (gameModes.Contains(Table.Cell(i, ColGameMode))) result.Add(i);
        }
        return result;
    }

    public List<int> ExceptGameModes(IReadOnlyCollection<string> gameModes)
    {
        var result = new List<int>();
        for (int i = 0; i < Table.Count; i++)
        {
            if (!IsSelectable(i)) continue;
            if (!gameModes.Contains(Table.Cell(i, ColGameMode))) result.Add(i);
        }
        return result;
    }

    public static readonly string[] SoloModes = ["BattleRoyale", "Survival", "BattleRoyaleTeam", "BossFight"];
    public static readonly string[] TicketModes = ["Survival", "BossFight"];
    public static readonly string[] ShowdownModes = ["BattleRoyale", "BattleRoyaleTeam"];

    public bool IsTrioMode(string gameMode) => !SoloModes.Contains(gameMode);
    public bool IsTicketMode(string gameMode) => TicketModes.Contains(gameMode);
}
