namespace GameLogic.Csv;

public readonly record struct MapPoint(int X, int Y);

public sealed class TileSet
{
    private readonly Dictionary<char, (bool Movement, bool Projectiles, bool Hides, bool Destructible, bool WeaponBreakable)> _tiles = [];

    public TileSet(CsvTable table)
    {
        for (int row = 0; row < table.Count; row++)
        {
            string code = table.Cell(row, "TileCode");
            if (code.Length != 1) continue;
            _tiles[code[0]] = (
                table.CellBool(row, "BlocksMovement"),
                table.CellBool(row, "BlocksProjectiles"),
                table.CellBool(row, "HidesHero"),
                table.CellBool(row, "IsDestructible"),
                table.CellBool(row, "IsDestructibleNormalWeapon"));
        }
    }

    public bool BlocksMovement(char code) => _tiles.TryGetValue(code, out var t) && t.Movement;
    public bool BlocksProjectiles(char code) => _tiles.TryGetValue(code, out var t) && t.Projectiles;
    public bool HidesHero(char code) => _tiles.TryGetValue(code, out var t) && t.Hides;
    public bool IsDestructible(char code) => _tiles.TryGetValue(code, out var t) && t.Destructible;
    public bool IsWeaponBreakable(char code) => _tiles.TryGetValue(code, out var t) && t.WeaponBreakable;
}

public sealed class BattleMap
{
    public const int TileSize = 300;

    public const int MaxEncodableX = (1 << 15) - 1;
    public const int MaxEncodableY = (1 << 16) - 1;

    public string Name { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int Width => Columns * TileSize;
    public int Height => Rows * TileSize;

    public IReadOnlyList<MapPoint> TeamSpawns { get; }
    public IReadOnlyList<MapPoint> EnemySpawns { get; }

    private readonly List<string> _grid;
    private readonly TileSet _tiles;
    private Dictionary<char, List<MapPoint>> _markers = [];

    internal BattleMap(string name, List<string> grid, TileSet tiles)
    {
        _grid = grid;
        _tiles = tiles;
        Name = name;
        Rows = grid.Count;
        Columns = grid.Count == 0 ? 0 : grid.Max(r => r.Length);

        var team = new List<MapPoint>();
        var enemy = new List<MapPoint>();
        _markers = [];
        for (int row = 0; row < grid.Count; row++)
        {
            string line = grid[row];
            for (int column = 0; column < line.Length; column++)
            {
                var point = new MapPoint(column * TileSize + TileSize / 2, row * TileSize + TileSize / 2);
                char code = line[column];
                if (code == '1') team.Add(point);
                else if (code == '2') enemy.Add(point);
                if (char.IsDigit(code))
                {
                    if (!_markers.TryGetValue(code, out var list)) _markers[code] = list = [];
                    list.Add(point);
                }
            }
        }

        TeamSpawns = team;
        EnemySpawns = enemy;
    }

    public IReadOnlyList<MapPoint> Markers(char code) =>
        _markers.TryGetValue(code, out var list) ? list : [];

    public MapPoint? MarkerCentre(char code)
    {
        var list = Markers(code);
        if (list.Count == 0) return null;
        return new MapPoint(list.Sum(p => p.X) / list.Count, list.Sum(p => p.Y) / list.Count);
    }

    public IReadOnlyList<MapPoint> SpawnsFor(int team)
    {
        var own = team == 0 ? TeamSpawns : EnemySpawns;
        if (own.Count > 0) return own;
        return TeamSpawns.Count > 0 ? TeamSpawns : EnemySpawns;
    }

    public MapPoint SpawnPoint(int team, int indexInTeam)
    {
        var points = SpawnsFor(team);
        if (points.Count == 0) return new MapPoint(Width / 2, Height / 2);
        return points[indexInTeam % points.Count];
    }

    public int SpawnRotation(int team)
    {
        var own = SpawnsFor(team);
        var other = SpawnsFor(team == 0 ? 1 : 0);
        if (own.Count == 0 || other.Count == 0) return 270;
        int ownY = own.Sum(p => p.Y) / own.Count;
        int otherY = other.Sum(p => p.Y) / other.Count;
        return otherY < ownY ? 270 : 90;
    }

    public int ClampX(int x) => Math.Clamp(x, 0, Math.Min(Width - 1, MaxEncodableX));
    public int ClampY(int y) => Math.Clamp(y, 0, Math.Min(Height - 1, MaxEncodableY));

    public char TileAt(int x, int y)
    {
        int column = x / TileSize;
        int row = y / TileSize;
        if (row < 0 || row >= _grid.Count) return '.';
        string line = _grid[row];
        return column < 0 || column >= line.Length ? '.' : line[column];
    }

    public bool BlocksMovement(int x, int y) => _tiles.BlocksMovement(TileAt(x, y));
    public bool BlocksProjectiles(int x, int y) => _tiles.BlocksProjectiles(TileAt(x, y));
    public bool HidesHero(int x, int y) => _tiles.HidesHero(TileAt(x, y));

    public char TileOf(int column, int row)
    {
        if (row < 0 || row >= _grid.Count) return '.';
        string line = _grid[row];
        return column < 0 || column >= line.Length ? '.' : line[column];
    }

    public bool IsDestructibleTile(int column, int row) => _tiles.IsDestructible(TileOf(column, row));

    public bool IsWeaponBreakable(int column, int row)
    {
        char code = TileOf(column, row);
        return _tiles.IsWeaponBreakable(code) && _tiles.IsDestructible(code);
    }
}

public sealed class MapCatalog
{
    private readonly Dictionary<string, BattleMap> _maps = new(StringComparer.OrdinalIgnoreCase);

    public MapCatalog(CsvTable table, TileSet tiles)
    {
        string? name = null;
        var grid = new List<string>();

        void Flush()
        {
            if (name is null || grid.Count == 0) return;
            var map = new BattleMap(name, grid, tiles);
            if (map.Columns > 0) _maps[name] = map;
        }

        for (int row = 0; row < table.Count; row++)
        {
            string group = table.Cell(row, 1).Trim();
            string data = table.Cell(row, 2);

            if (group.Length > 0)
            {
                Flush();
                name = group;
                grid = [];
            }

            if (name is not null && data.Length > 0) grid.Add(data);
        }
        Flush();
    }

    public int Count => _maps.Count;

    public BattleMap? Find(string name) =>
        name.Length > 0 && _maps.TryGetValue(name, out var map) ? map : null;

    public IEnumerable<string> Names => _maps.Keys;
}
