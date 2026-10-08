namespace GameLogic.Csv;

public sealed class GameAssets
{
    private readonly Dictionary<string, CsvTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public GameAssets(string assetsRoot)
    {
        _root = assetsRoot;
        Characters = new Characters(Table("characters"));
        Cards = new Cards(Table("cards"), Characters);
        Maps = new MapCatalog(Table("maps"), new TileSet(Table("tiles")));
        Weapons = new Weapons(Table("skills"), Table("projectiles"), Table("area_effects"), Characters);
        Items = new BattleItems(Table("items"));
        Weapons.Bind(Characters, Items);
        Locations = new Locations(Table("locations"), Maps);
        Skins = new Skins(Table("skins"), Characters);
        Thumbnails = new PlayerThumbnails(Table("player_thumbnails"), Characters);
    }

    public Characters Characters { get; }
    public Cards Cards { get; }
    public Locations Locations { get; }
    public Skins Skins { get; }
    public PlayerThumbnails Thumbnails { get; }
    public MapCatalog Maps { get; }
    public Weapons Weapons { get; }
    public BattleItems Items { get; }

    public BattleMap? MapForLocation(int locationId) => Maps.Find(Locations.GetAllowedMap(locationId));

    public CsvTable Table(string name)
    {
        if (_tables.TryGetValue(name, out var cached)) return cached;
        var path = Path.Combine(_root, "csv_logic", name + ".csv");
        if (!File.Exists(path)) throw new FileNotFoundException($"Нет таблицы {name}", path);
        var table = CsvTable.Load(path);
        _tables[name] = table;
        return table;
    }
}
