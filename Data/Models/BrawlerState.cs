namespace Data.Models;

public sealed class BrawlerState
{
    public Dictionary<string, int> Cards { get; set; } = new();
    public List<int> Skins { get; set; } = [0];
    public int SelectedSkin { get; set; }
    public int Trophies { get; set; }
    public int HighestTrophies { get; set; }
    public int PowerLevel { get; set; }
    public int PowerPoints { get; set; }
    public int State { get; set; }
    public int StarPower { get; set; }

    public static BrawlerState NewlyUnlocked(string cardId) => new()
    {
        Cards = new Dictionary<string, int> { [cardId] = 1 },
        State = 0,
    };
}
