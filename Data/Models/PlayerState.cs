namespace Data.Models;

public sealed class PlayerState
{
    public string Token { get; set; } = string.Empty;
    public int LowId { get; set; }
    public string Name { get; set; } = "Brawler";

    public int ClubId { get; set; }
    public int ClubRole { get; set; }
    public int TeamId { get; set; }

    public int Experience { get; set; }
    public int Trophies { get; set; }
    public int HighestTrophies { get; set; }
    public int TrophyRank { get; set; } = 1;
    public int TrophiesReward { get; set; }

    public int SoloWins { get; set; }
    public int DuoWins { get; set; }
    public int TrioWins { get; set; }

    public int Gems { get; set; }
    public int Gold { get; set; }
    public int Tickets { get; set; }
    public int UpgradeTokens { get; set; }
    public int CoinsReward { get; set; }
    public int CoinsDoubler { get; set; }
    public int CoinsBooster { get; set; }

    public int ProfileIcon { get; set; }
    public int TutorialState { get; set; }
    public string Region { get; set; } = "EN";
    public int ControlMode { get; set; }
    public bool HasBattleHints { get; set; }
    public int PlayerStatus { get; set; }
    public long LastConnectionTime { get; set; }
    public int BattleId { get; set; }
    public bool Banned { get; set; }
    public int BestTimeBoss { get; set; }
    public int BestTimeSurvival { get; set; }

    public Dictionary<string, BrawlerState> Brawlers { get; set; } = new();
    public Dictionary<string, object> HomeNotifications { get; set; } = new();
    public List<int> PlayerUpgrades { get; set; } = [];
    public Dictionary<string, object> Friends { get; set; } = new();

    public static PlayerState CreateNew(string token, int lowId, int tutorialState) => new()
    {
        Token = token,
        LowId = lowId,
        TutorialState = tutorialState,
        Gold = 92,
        Brawlers = new Dictionary<string, BrawlerState>
        {
            ["0"] = new() { Cards = new Dictionary<string, int> { ["0"] = 1 }, State = 2 },
        },
    };
}
