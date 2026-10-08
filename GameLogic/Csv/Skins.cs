namespace GameLogic.Csv;

public sealed class Skins(CsvTable table, Characters characters)
{
    private const int ColName = 0;
    private const int ColTarget = 1;
    private const int ColPrice = 3;
    private const int CharactersColDefaultSkin = 21;

    private readonly HashSet<string> _defaultSkinNames = BuildDefaults(characters);

    public CsvTable Table { get; } = table;
    public int Count => Table.Count;

    private static HashSet<string> BuildDefaults(Characters characters)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < characters.Table.Count; i++)
        {
            var name = characters.Table.Cell(i, CharactersColDefaultSkin);
            if (name.Length != 0) set.Add(name);
        }
        return set;
    }

    public bool Exists(int skinId) => skinId >= 0 && skinId < Table.Count;

    public int GetBrawlerId(int skinId)
    {
        if (!Exists(skinId)) return 0;
        var target = Table.Cell(skinId, ColTarget);
        return characters.GetIdByName(target) ?? 0;
    }

    public int GetPrice(int skinId) => Exists(skinId) ? Table.CellInt(skinId, ColPrice) : 0;

    public bool IsDefault(int skinId) =>
        Exists(skinId) && _defaultSkinNames.Contains(Table.Cell(skinId, ColName));
}

public sealed class PlayerThumbnails(CsvTable table, Characters characters)
{
    private const int ColRequiredExpLevel = 1;
    private const int ColRequiredTrophies = 2;
    private const int ColRequiredBrawler = 4;

    public CsvTable Table { get; } = table;

    public bool Exists(int id) => id >= 0 && id < Table.Count;

    public int RequiredTrophies(int id) => Exists(id) ? Table.CellInt(id, ColRequiredTrophies) : 0;

    public int RequiredExpLevel(int id) => Exists(id) ? Table.CellInt(id, ColRequiredExpLevel) : 0;

    public int? RequiredBrawlerId(int id)
    {
        if (!Exists(id)) return null;
        var name = Table.Cell(id, ColRequiredBrawler);
        return name.Length == 0 ? null : characters.GetIdByName(name);
    }
}
