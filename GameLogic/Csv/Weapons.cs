namespace GameLogic.Csv;

public static class AreaEffectTypes
{
    public const int Damage = 0;
    public const int Dot = 2;
    public const int DelayedDamage = 8;
}

public sealed record AreaEffectStats(int CsvId, string Name, int Radius, int TimeMs, int Type, int Damage);

public sealed record ProjectileStats(
    int CsvId,
    string Name,
    int SpeedPerSecond,
    int Radius,
    bool IsIndirect,
    bool IsBouncing,
    int TriggerWithDelayMs,
    int PreExplosionTimeMs,
    AreaEffectStats? AreaEffect);

public sealed record WeaponStats(
    int Range,
    int DamagePerBullet,
    int Bullets,
    int SpreadDegrees,
    int CooldownMs,
    int ActiveTimeMs,
    int MsBetweenAttacks,
    int RechargeMs,
    int MaxCharge,
    bool IsCharge,
    int ChargeSpeed,
    int ChargeType,
    int SummonedCharacter,
    int SummonHitpoints,
    bool SummonUsesShortForm,
    int SpawnedItem,
    int SpawnedItemCount,
    int SpawnedItemDamage,
    int SpawnedItemTrigger,
    AreaEffectStats? SpawnedItemBlast,
    ProjectileStats? Projectile)
{
    public static readonly WeaponStats Fallback =
        new(2600, 700, 1, 0, 250, 150, 100, 1500, 3, false, 0, 0, -1, 0, true, -1, 0, 0, 0, null, null);

    public int Volleys => Math.Max(1, ActiveTimeMs / Math.Max(1, MsBetweenAttacks));

    public bool IsInstantCharge => IsCharge && ChargeType == 4;
}

public sealed class BattleItems
{
    private readonly Dictionary<string, int> _rows = new(StringComparer.OrdinalIgnoreCase);

    private readonly CsvTable _table;

    public BattleItems(CsvTable table)
    {
        _table = table;
        for (int i = 0; i < table.Count; i++) _rows.TryAdd(table.Cell(i, 0), i);
        GemSpawner = Row("OrbSpawner");
        Gem = Row("Point");
        BountyStar = Row("Money");
        PowerCube = Row("BattleRoyaleBuff");
    }

    public int ValueOf(int row) => row < 0 ? 0 : _table.CellInt(row, "Value");

    public int TriggerRange(int row) => row < 0 ? 0 : _table.CellInt(row, "TriggerRangeSubTiles");

    public string TriggerEffect(int row) => row < 0 ? string.Empty : _table.Cell(row, "TriggerAreaEffect");

    public (int Row, int Count) Unpack(int boxRow)
    {
        if (boxRow < 0) return (-1, 0);
        string name = _table.Cell(boxRow, 0);
        if (!name.StartsWith("BoxOf", StringComparison.Ordinal)) return (boxRow, 1);

        string inner = name["BoxOf".Length..].TrimEnd('s');
        int row = Row(inner);
        return row < 0 ? (boxRow, 1) : (row, Math.Max(1, ValueOf(boxRow)));
    }

    public int GemSpawner { get; }
    public int Gem { get; }
    public int BountyStar { get; }
    public int PowerCube { get; }

    private int Row(string name) => _rows.TryGetValue(name, out int row) ? row : -1;

    public int RowOfName(string name) => Row(name);
}

public sealed class Weapons
{
    private const int RangeUnitsPerPoint = 100;

    private readonly CsvTable _skills;
    private readonly CsvTable _projectiles;
    private readonly CsvTable _areaEffects;
    private readonly Dictionary<string, int> _areaEffectRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Characters _characters;
    private readonly Dictionary<string, int> _skillRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _projectileRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WeaponStats> _cache = [];
    private Characters? _roster;
    private BattleItems? _items;

    public Weapons(CsvTable skills, CsvTable projectiles, CsvTable areaEffects, Characters characters)
    {
        _skills = skills;
        _projectiles = projectiles;
        _areaEffects = areaEffects;
        _characters = characters;
        for (int i = 0; i < areaEffects.Count; i++) _areaEffectRows.TryAdd(areaEffects.Cell(i, 0), i);
        for (int i = 0; i < skills.Count; i++) _skillRows.TryAdd(skills.Cell(i, 0), i);
        for (int i = 0; i < projectiles.Count; i++) _projectileRows.TryAdd(projectiles.Cell(i, 0), i);
    }

    public void Bind(Characters roster, BattleItems items)
    {
        _roster = roster;
        _items = items;
    }

    public WeaponStats ForBrawler(int brawlerId) => ForSkill(_characters.GetWeaponSkillName(brawlerId));

    public WeaponStats UltiForBrawler(int brawlerId) => ForSkill(_characters.GetUltimateSkillName(brawlerId));

    public int WeaponRow(int brawlerId) => RowOf(_characters.GetWeaponSkillName(brawlerId));

    public int UltiRow(int brawlerId) => RowOf(_characters.GetUltimateSkillName(brawlerId));

    public int RowOf(string skillName) => _skillRows.TryGetValue(skillName, out int row) ? row : -1;

    public WeaponStats ForSkill(string skillName)
    {
        if (skillName.Length == 0) return WeaponStats.Fallback;
        if (_cache.TryGetValue(skillName, out var cached)) return cached;
        var stats = Build(skillName);
        _cache[skillName] = stats;
        return stats;
    }

    private WeaponStats Build(string skillName)
    {
        if (skillName.Length == 0 || !_skillRows.TryGetValue(skillName, out int row))
            return WeaponStats.Fallback;

        int range = _skills.CellInt(row, "CastingRange") * RangeUnitsPerPoint;
        int damage = _skills.CellInt(row, "Damage");
        int bullets = Math.Max(1, _skills.CellInt(row, "NumBulletsInOneAttack"));
        int spread = _skills.CellInt(row, "Spread") / 2;
        int cooldown = _skills.CellInt(row, "Cooldown");
        int activeTime = _skills.CellInt(row, "ActiveTime");
        int betweenAttacks = _skills.CellInt(row, "MsBetweenAttacks");
        int recharge = _skills.CellInt(row, "RechargeTime");
        int charge = Math.Max(1, _skills.CellInt(row, "MaxCharge"));

        return new WeaponStats(
            range > 0 ? range : WeaponStats.Fallback.Range,
            damage > 0 ? damage : WeaponStats.Fallback.DamagePerBullet,
            bullets,
            spread,
            cooldown > 0 ? cooldown : WeaponStats.Fallback.CooldownMs,
            activeTime > 0 ? activeTime : WeaponStats.Fallback.ActiveTimeMs,
            betweenAttacks > 0 ? betweenAttacks : WeaponStats.Fallback.MsBetweenAttacks,
            recharge > 0 ? recharge : WeaponStats.Fallback.RechargeMs,
            charge,
            _skills.Cell(row, "BehaviorType") == "Charge",
            _skills.CellInt(row, "ChargeSpeed"),
            _skills.CellInt(row, "ChargeType"),
            SummonRow(_skills.Cell(row, "SummonedCharacter")),
            SummonHp(_skills.Cell(row, "SummonedCharacter")),
            SummonShortForm(_skills.Cell(row, "SummonedCharacter")),
            Spawned(_skills.Cell(row, "SpawnedItem")).Row,
            Spawned(_skills.Cell(row, "SpawnedItem")).Count,
            Spawned(_skills.Cell(row, "SpawnedItem")).Damage,
            Spawned(_skills.Cell(row, "SpawnedItem")).Trigger,
            Spawned(_skills.Cell(row, "SpawnedItem")).Blast,
            FindProjectile(_skills.Cell(row, "Projectile")));
    }

    private (int Row, int Count, int Damage, int Trigger, AreaEffectStats? Blast) Spawned(string name)
    {
        if (_items is null || name.Length == 0) return (-1, 0, 0, 0, null);
        var (row, count) = _items.Unpack(_items.RowOfName(name));
        int trigger = Math.Clamp(_items.TriggerRange(row) * 100, 200, 900);
        return (row, count, _items.ValueOf(row), trigger, FindAreaEffect(_items.TriggerEffect(row)));
    }

    private int SummonRow(string name) => _roster?.RowOfName(name) ?? -1;

    private bool SummonShortForm(string name)
    {
        int row = SummonRow(name);
        return row < 0 || (_roster?.UsesShortBattleForm(row) ?? true);
    }

    private int SummonHp(string name)
    {
        int row = SummonRow(name);
        return row < 0 ? 0 : Math.Clamp(_roster!.GetHitpoints(row), 500, 8191);
    }

    private ProjectileStats? FindProjectile(string name)
    {
        if (name.Length == 0 || !_projectileRows.TryGetValue(name, out int row)) return null;
        return new ProjectileStats(
            row,
            name,
            _projectiles.CellInt(row, "Speed"),
            _projectiles.CellInt(row, "Radius"),
            _projectiles.CellBool(row, "Indirect"),
            _projectiles.CellBool(row, "IsBouncing"),
            _projectiles.CellInt(row, "TriggerWithDelayMs"),
            _projectiles.CellInt(row, "PreExplosionTimeMs"),
            FindAreaEffect(_projectiles.Cell(row, "SpawnAreaEffectObject")));
    }

    private AreaEffectStats? FindAreaEffect(string name)
    {
        if (name.Length == 0 || !_areaEffectRows.TryGetValue(name, out int row)) return null;
        return new AreaEffectStats(
            row,
            name,
            _areaEffects.CellInt(row, "Radius"),
            _areaEffects.CellInt(row, "TimeMs"),
            TypeOf(_areaEffects.Cell(row, "Type")),
            _areaEffects.CellInt(row, "Damage"));
    }

    private static int TypeOf(string type) => type switch
    {
        "Damage" => 0,
        "SmokeScreen" => 1,
        "Dot" => 2,
        "Heal" => 3,
        "Hot" => 4,
        "BulletExplosion" => 5,
        "Effect" => 6,
        "Pushback" => 7,
        "DelayedDamage" => 8,
        _ => 0,
    };
}
