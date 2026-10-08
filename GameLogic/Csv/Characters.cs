namespace GameLogic.Csv;

public sealed class Characters(CsvTable table)
{
    private readonly Dictionary<string, int> _byName = BuildNameIndex(table);

    public CsvTable Table { get; } = table;
    public int Count => Table.Count;

    private static Dictionary<string, int> BuildNameIndex(CsvTable table)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < table.Count; i++)
        {
            var name = table.Cell(i, 0);
            if (name.Length != 0) map.TryAdd(name, i);
        }
        return map;
    }

    public bool IsDisabled(string name) =>
        _byName.TryGetValue(name, out var index) && Table.CellBool(index, "Disabled");

    public int? GetIdByName(string name) =>
        _byName.TryGetValue(name, out var index) ? index : null;

    public string GetName(int id) => Table.Cell(id, 0);

    public int GetHitpoints(int id) => Table.CellInt(id, 7);

    public string GetWeaponSkillName(int id) => Table.Cell(id, "WeaponSkill").Trim();

    public string GetUltimateSkillName(int id) => Table.Cell(id, "UltimateSkill").Trim();

    public bool UsesShortBattleForm(int id) =>
        Table.CellInt(id, "Speed") == 0 && Table.CellInt(id, "AutoAttackDamage") == 0;

    public int GetUltiChargeMultiplier(int id)
    {
        int value = Table.CellInt(id, "UltiChargeMul");
        return value > 0 ? value : 100;
    }

    public int RowOfName(string name)
    {
        if (name.Length == 0) return -1;
        for (int i = 0; i < Table.Count; i++)
            if (Table.Cell(i, 0) == name) return i;
        return -1;
    }

    public int SafeRow(int index)
    {
        string name = $"Safe{index + 1}";
        for (int i = 0; i < Table.Count; i++)
            if (Table.Cell(i, 0) == name) return i;
        return -1;
    }

    public int GetSpeed(int id)
    {
        int speed = Table.CellInt(id, "Speed");
        return speed > 0 ? speed : 650;
    }

    public List<int> GetPlayableBrawlers()
    {
        var result = new List<int>();
        for (int i = 0; i < Table.Count; i++)
        {
            if (Table.Cell(i, 19) == "Hero" && !Table.CellBool(i, "Disabled"))
                result.Add(i);
        }
        return result;
    }
}
