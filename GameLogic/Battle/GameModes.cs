namespace GameLogic.Battle;

public static class GameModes
{
    public const int CoinRush = 0;
    public const int AttackDefend = 2;
    public const int BountyHunter = 3;
    public const int Artifact = 4;
    public const int LaserBall = 5;
    public const int BattleRoyale = 6;
    public const int BossFight = 7;
    public const int Survival = 8;
    public const int BattleRoyaleTeam = 9;

    public static int Variation(string gameMode) => gameMode switch
    {
        "CoinRush" => CoinRush,
        "AttackDefend" => AttackDefend,
        "BountyHunter" => BountyHunter,
        "Artifact" => Artifact,
        "LaserBall" => LaserBall,
        "BattleRoyale" => BattleRoyale,
        "BossFight" => BossFight,
        "Survival" => Survival,
        "BattleRoyaleTeam" => BattleRoyaleTeam,
        _ => CoinRush,
    };

    public static bool IsSolo(int variation) =>
        variation is BattleRoyale or BattleRoyaleTeam or Survival or BossFight;

    public static bool NoRespawn(int variation) =>
        variation is BattleRoyale or BattleRoyaleTeam;

    public static int PlayersPerMatch(int variation, int fallback) => variation switch
    {
        BattleRoyale or BattleRoyaleTeam => 10,
        _ => fallback,
    };

    public static int BattleTicks(int variation) => variation switch
    {
        BattleRoyale or BattleRoyaleTeam => 16000,
        LaserBall => 4280,
        BossFight => 9680,
        _ => 3080,
    };
}
