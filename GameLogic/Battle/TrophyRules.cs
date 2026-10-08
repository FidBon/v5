namespace GameLogic.Battle;

public readonly record struct BattleRewards(int Trophies, int Coins, int Experience, int StarExperience);

public static class TrophyRules
{
    private static readonly (int Low, int High, int[] Values)[] TrioTable =
    [
        (0, 29, [6, 0, 0]),
        (30, 59, [6, -1, 0]),
        (60, 99, [6, -2, 0]),
        (100, 139, [5, -2, 0]),
        (140, 219, [5, -3, 0]),
        (220, 299, [5, -4, 0]),
        (300, 499, [5, -5, 0]),
        (500, 599, [4, -6, 0]),
        (600, 699, [3, -6, 0]),
        (700, 799, [3, -7, 0]),
        (800, 899, [2, -7, 0]),
        (900, int.MaxValue, [2, -8, 0]),
    ];

    private static readonly (int Low, int High, int[] Values)[] SoloTable =
    [
        (0, 29, [8, 7, 6, 5, 5, 4, 3, 2, 1, 0]),
        (30, 59, [8, 7, 6, 4, 3, 2, 0, -1, -2, -4]),
        (60, 99, [8, 7, 5, 4, 3, 1, 0, -2, -3, -4]),
        (100, 139, [7, 6, 5, 3, 2, 1, -1, -2, -3, -4]),
        (140, 219, [7, 6, 4, 3, 2, 0, -1, -3, -4, -5]),
        (220, 299, [7, 6, 4, 3, 1, 0, -2, -3, -5, -6]),
        (300, 419, [7, 6, 4, 2, 1, -1, -2, -4, -6, -7]),
        (420, 499, [5, 4, 3, 1, 0, -2, -3, -4, -6, -7]),
        (500, 599, [5, 3, 2, 1, -1, -2, -3, -4, -6, -7]),
        (600, 699, [4, 3, 1, 0, -1, -1, -3, -5, -6, -7]),
        (700, 799, [4, 2, 1, -1, -2, -3, -4, -5, -6, -8]),
        (800, 899, [3, 2, 0, -1, -2, -3, -4, -5, -7, -8]),
        (900, int.MaxValue, [3, 1, -1, -2, -3, -4, -5, -6, -7, -8]),
    ];

    private static readonly int[] TrioCoins = [20, 15, 10];
    private static readonly int[] SoloCoins = [34, 28, 22, 16, 12, 8, 6, 4, 2, 1, 1];
    private static readonly int[] TrioExp = [10, 5, 0];
    private static readonly int[] SoloExp = [15, 12, 9, 6, 5, 4, 2, 1, 0, 0, 0];

    public static BattleRewards Calculate(bool isTrio, int outcomeOrRank, int brawlerTrophies, bool isTicketEvent)
    {
        if (isTicketEvent)
            return new BattleRewards(0, 12, 12, 0);

        if (isTrio)
        {
            int index = Math.Clamp(outcomeOrRank, 0, TrioCoins.Length - 1);
            return new BattleRewards(
                Lookup(TrioTable, brawlerTrophies, index),
                TrioCoins[index],
                TrioExp[index],
                10);
        }

        int rankIndex = Math.Clamp(outcomeOrRank - 1, 0, SoloCoins.Length - 1);
        return new BattleRewards(
            Lookup(SoloTable, brawlerTrophies, rankIndex),
            SoloCoins[rankIndex],
            SoloExp[rankIndex],
            10);
    }

    private static int Lookup((int Low, int High, int[] Values)[] table, int trophies, int index)
    {
        foreach (var (low, high, values) in table)
        {
            if (trophies < low || trophies > high) continue;
            return index >= 0 && index < values.Length ? values[index] : 0;
        }
        return 0;
    }
}
