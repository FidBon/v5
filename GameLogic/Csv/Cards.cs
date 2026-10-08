namespace GameLogic.Csv;

public sealed class Cards(CsvTable table, Characters characters)
{
    private const int ColTarget = 3;
    private const int ColType = 6;
    private const string ColRarity = "Rarity";

    public CsvTable Table { get; } = table;

    public bool IsUnlockCard(int cardId) => Table.Cell(cardId, ColType) == "unlock";

    public string GetTargetName(int cardId) => Table.Cell(cardId, ColTarget);

    public string GetRarity(int cardId) => Table.Cell(cardId, ColRarity);

    public int? GetBrawlerId(int cardId)
    {
        var target = GetTargetName(cardId);
        return target.Length == 0 ? null : characters.GetIdByName(target);
    }

    public List<int> GetUnlockCards()
    {
        var result = new List<int>();
        for (int i = 0; i < Table.Count; i++)
        {
            if (Table.Cell(i, ColType) != "unlock") continue;
            if (characters.IsDisabled(Table.Cell(i, ColTarget))) continue;
            result.Add(i);
        }
        return result;
    }

    public List<int> GetUnlockCardsByRarity(string rarity)
    {
        var result = new List<int>();
        for (int i = 0; i < Table.Count; i++)
        {
            if (Table.Cell(i, ColType) != "unlock") continue;
            if (!string.Equals(Table.Cell(i, ColRarity), rarity, StringComparison.Ordinal)) continue;
            if (characters.IsDisabled(Table.Cell(i, ColTarget))) continue;
            result.Add(i);
        }
        return result;
    }

    public List<int> GetUpgradeCards(int brawlerId)
    {
        var name = characters.GetName(brawlerId);
        var result = new List<int>();
        for (int i = 0; i < Table.Count; i++)
        {
            if (Table.Cell(i, ColTarget) != name) continue;
            if (Table.Cell(i, ColType) == "unlock") continue;
            result.Add(i);
        }
        return result;
    }

    public static readonly string[] RarityOrder =
        ["common", "rare", "super_rare", "epic", "mega_epic", "legendary"];

    public static string RarityById(int rarity) =>
        rarity >= 0 && rarity < RarityOrder.Length ? RarityOrder[rarity] : "legendary";
}
