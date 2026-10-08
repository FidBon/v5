using GameLogic.Battle;
using GameLogic.Csv;

namespace Battle;

public sealed class ObjectTransform
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Index { get; set; }
    public int Visibility { get; set; } = 15;
}

public sealed class SkillState
{
    public string Name { get; init; } = "Weapon";
    public int ActiveTicks { get; set; }
    public bool Active { get; set; }
    public int Unknown { get; set; }
    public int Ammo { get; set; } = 3000;
}

public sealed class BattleHero
{
    public const int FullUltiCharge = 4000;

    public int Slot { get; init; }

    public int IndexInTeam { get; init; }

    public int BrawlerId { get; init; }

    public int SpeedPerSecond { get; init; } = 650;
    public WeaponStats Weapon { get; init; } = WeaponStats.Fallback;
    public WeaponStats Ulti { get; init; } = WeaponStats.Fallback;
    public int WeaponRow { get; init; } = -1;
    public int UltiRow { get; init; } = -1;
    public WeaponStats BurstStats { get; set; } = WeaponStats.Fallback;
    public int UltiActiveTicks { get; set; }
    public int UltiChargeMultiplier { get; init; } = 100;
    public int DashTicksLeft { get; set; }
    public int DashTargetX { get; set; }
    public int DashTargetY { get; set; }
    public int DashStep { get; set; }
    public int DashDamage { get; set; }
    public HashSet<int> DashHits { get; } = [];
    public int WeaponRange { get; set; } = WeaponStats.Fallback.Range;
    public int WeaponCooldownTicks { get; init; } = 5;
    public int AmmoPerTick { get; init; } = 33;
    public int MaxAmmo { get; init; } = 3000;
    public int CooldownTicks { get; set; }
    public int VolleyIntervalTicks { get; init; } = 2;
    public int BurstVolleysLeft { get; set; }
    public int BurstTimer { get; set; }
    public int BurstAim { get; set; }
    public int BurstAimX { get; set; }
    public int BurstAimY { get; set; }
    public int FreezeInputTicks { get; set; }
    public int DetourTicks { get; set; }
    public int DetourAngle { get; set; }

    public int PowerLevel { get; init; }
    public int PowerCubes { get; set; }
    public int BaseHitpoints { get; init; } = 3600;
    public int SkinId { get; init; }
    public int Team { get; init; }
    public string Name { get; init; } = "Bot";
    public int OwnerLowId { get; init; }
    public bool IsBot => OwnerLowId == 0;

    public ObjectTransform Transform { get; } = new();

    public int MaxHitpoints { get; set; } = 3600;
    public int Hitpoints { get; set; } = 3600;
    public bool Alive => Hitpoints > 0;

    public int State { get; set; }
    public int TeamRotation { get; set; }
    public int EnemyRotation { get; set; }
    public int PlayedAnimation { get; set; } = 63;

    public bool Slowed { get; set; }
    public bool PlayingAnimation { get; set; }
    public bool Stunned { get; set; }
    public bool Poisoned { get; set; }
    public bool HasBall { get; set; }
    public bool ImmunityShield { get; set; }
    public bool Rage { get; set; }
    public bool UltiAiming { get; set; }
    public bool UltiActive { get; set; }
    public bool Invisible { get; set; }
    public bool NotFullyVisible { get; set; }

    public int ItemsCarried { get; set; }
    public int UltiCharge { get; set; }

    public int Score { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public List<KillFeedEntry> KillFeed { get; } = [];

    public int AttackTicks { get; set; }

    public int RespawnTicks { get; set; }

    public List<SkillState> Skills { get; } =
    [
        new SkillState { Name = "Weapon", Ammo = 3000 },
        new SkillState { Name = "Ulti" },
    ];

    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public bool HasMoveOrder { get; set; }
}

public readonly record struct KillFeedEntry(int VictimIndex, int Stars, int Tick);

public sealed class BattleItem
{
    public int Id { get; init; }
    public int CsvId { get; init; }
    public bool IsSpawner { get; init; }
    public bool IsCollectable { get; init; }
    public bool IsPowerCube { get; init; }
    public int OwnerSlot { get; init; }
    public int Team { get; init; }
    public int Damage { get; init; }
    public int BlastRadius { get; init; }
    public int TriggerRadius { get; init; }
    public AreaEffectStats? Blast { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class BattleStructure
{
    public int Id { get; init; }
    public int OwnerSlot { get; init; }
    public int CsvId { get; init; }
    public int Team { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int MaxHitpoints { get; init; }
    public int Hitpoints { get; set; }
    public int Drops { get; init; }
    public bool Hidden { get; init; }
    public bool Alive => Hitpoints > 0;
    public int HealthPercent => MaxHitpoints <= 0 ? 0 : Math.Clamp(Hitpoints * 100 / MaxHitpoints, 0, 100);
}

public sealed class BattleBall
{
    public int X { get; set; }
    public int Y { get; set; }
    public int CarrierSlot { get; set; }
    public int LastTouchSlot { get; set; }
    public int LastTouchTeam { get; set; }
    public int DirectionDegrees { get; set; }
    public int StepPerTick { get; set; }
    public int RemainingRange { get; set; }
    public int FrozenTicks { get; set; }
    public int CelebrateTicks { get; set; }
}

public sealed class BattleAreaEffect
{
    public int Id { get; init; }
    public int CsvId { get; init; }
    public int OwnerSlot { get; init; }
    public int Team { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Radius { get; init; }
    public int Type { get; init; }
    public int TickDamage { get; init; }
    public int TicksLeft { get; set; }
    public int DamageTimer { get; set; }
}

public sealed class BattleProjectile
{
    public int Id { get; init; }
    public int CsvId { get; init; }
    public int OwnerSlot { get; init; }
    public int Team { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public int DirectionDegrees { get; init; }
    public int TargetX { get; init; }
    public int TargetY { get; init; }
    public int StepPerTick { get; init; }
    public int RemainingRange { get; set; }
    public int LaunchRange { get; init; }
    public int Z { get; set; }
    public int Damage { get; init; }
    public int HitRadius { get; init; }
    public bool IsIndirect { get; init; }
    public int BlastRadius { get; init; }
    public bool IsBouncing { get; init; }
    public bool HasTriggerDelay { get; init; }
    public bool HasPreExplosion { get; init; }
    public AreaEffectStats? AreaEffect { get; init; }
    public int State { get; set; }
    public int LingerTicks { get; set; }
}

public sealed class BattleState
{
    public int GlobalId { get; set; } = 2_000_000;
    public int FadeCounter { get; set; }
    public int TicksLeft { get; set; }
    public bool GameOver { get; set; }

    public List<BattleHero> Heroes { get; } = [];
    public List<BattleProjectile> Projectiles { get; } = [];
    public int NextProjectileId { get; set; }
    public List<BattleItem> Items { get; } = [];
    public int NextItemId { get; set; }
    public List<BattleAreaEffect> AreaEffects { get; } = [];
    public int NextAreaEffectId { get; set; }
    public int GemCsvId { get; set; } = -1;
    public int CubeCsvId { get; set; } = -1;
    public HashSet<int> DestroyedTiles { get; } = [];
    public List<BattleStructure> Structures { get; } = [];
    public BattleBall? Ball { get; set; }
    public int[] GoalX { get; } = new int[2];
    public int[] GoalY { get; } = new int[2];
    public int GoalSignal { get; set; }
    public int ViewerSlot { get; set; } = 1;

    public bool TileDestroyed(int column, int row) =>
        Map is { } map && DestroyedTiles.Contains(row * map.Columns + column);

    public bool DestroyTile(int column, int row) =>
        Map is { } map && DestroyedTiles.Add(row * map.Columns + column);

    public bool BlocksProjectiles(int x, int y) =>
        Map is { } map
        && map.BlocksProjectiles(x, y)
        && !TileDestroyed(x / BattleMap.TileSize, y / BattleMap.TileSize);

    public bool BlocksMovement(int x, int y) =>
        Map is { } map
        && map.BlocksMovement(x, y)
        && !TileDestroyed(x / BattleMap.TileSize, y / BattleMap.TileSize);
    public int[] TeamScores { get; } = new int[16];

    public int Ticks { get; set; }
    public string GameMode { get; set; } = "CoinRush";
    public int LocationId { get; set; }

    public BattleMap? Map { get; set; }

    public int ModeVariation => GameModes.Variation(GameMode);

    public bool IsLargeMap => Map is { Columns: > 20 };

    public int[] ObjectiveHealth { get; } = [100, 100];

    public BattleHero? HeroOf(int lowId) => Heroes.FirstOrDefault(h => h.OwnerLowId == lowId);
}
