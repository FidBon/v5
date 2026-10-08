using System.Collections.Concurrent;
using GameLogic.Battle;
using GameLogic.Csv;

namespace Battle;

public sealed record MatchRequest(int LowId, string Name, int BrawlerId, int SkinId, int PowerLevel, int LocationId, IBattleClient Client);

public sealed class MatchmakerOptions
{
    public bool ShowdownBoxes { get; init; } = true;
    public int PlayersPerMatch { get; init; } = 6;
    public TimeSpan FillWithBotsAfter { get; init; } = TimeSpan.FromSeconds(12);
    public int MaxTicks { get; init; } = 2400;
}

public sealed class Matchmaker(GameAssets assets, MatchmakerOptions options)
{
    private readonly ConcurrentDictionary<int, BattleRoom> _rooms = new();
    private readonly ConcurrentDictionary<int, BattleRoom> _roomByPlayer = new();
    private readonly List<(MatchRequest Request, DateTimeOffset QueuedAt)> _queue = [];
    private readonly object _queueLock = new();
    private readonly ConcurrentQueue<List<MatchRequest>> _parties = new();
    private int _nextRoomId;

    public int QueuedPlayers { get { lock (_queueLock) return _queue.Count; } }
    public int ActiveRooms => _rooms.Count;

    public BattleRoom? RoomOf(int lowId) =>
        _roomByPlayer.TryGetValue(lowId, out var room) && _rooms.ContainsKey(room.Id) ? room : null;

    public void LeaveRoom(int lowId)
    {
        if (!_roomByPlayer.TryRemove(lowId, out var room)) return;
        room.RemoveClient(lowId);
    }

    public void Enqueue(MatchRequest request)
    {
        lock (_queueLock)
        {
            _queue.RemoveAll(e => e.Request.LowId == request.LowId);
            _queue.Add((request, DateTimeOffset.UtcNow));
        }
    }

    public void Cancel(int lowId)
    {
        lock (_queueLock) _queue.RemoveAll(e => e.Request.LowId == lowId);
    }

    public void StartParty(List<MatchRequest> players)
    {
        foreach (var player in players) Cancel(player.LowId);
        _parties.Enqueue(players);
    }

    public List<(BattleRoom Room, List<MatchRequest> Players)> FormMatches(DateTimeOffset now)
    {
        var formed = new List<(BattleRoom, List<MatchRequest>)>();

        while (_parties.TryDequeue(out var party))
            formed.Add((BuildRoom(party), party));

        lock (_queueLock)
        {
            foreach (var location in _queue.Select(e => e.Request.LocationId).Distinct().ToList())
            {
                int seats = SeatsFor(location);

                while (true)
                {
                    var waiting = _queue
                        .Where(e => e.Request.LocationId == location)
                        .OrderBy(e => e.QueuedAt)
                        .ToList();
                    if (waiting.Count == 0) break;

                    bool full = waiting.Count >= seats;
                    bool waitedEnough = now - waiting[0].QueuedAt >= options.FillWithBotsAfter;
                    if (!full && !waitedEnough) break;

                    var batch = waiting.Take(Math.Min(seats, waiting.Count)).ToList();
                    foreach (var entry in batch) _queue.Remove(entry);

                    formed.Add((BuildRoom(batch.Select(e => e.Request).ToList()), batch.Select(e => e.Request).ToList()));
                }
            }
        }
        return formed;
    }

    public int SeatsFor(int locationId) => GameModes.PlayersPerMatch(
        GameModes.Variation(assets.Locations.GetGameMode(locationId)),
        options.PlayersPerMatch);

    private BattleRoom BuildRoom(List<MatchRequest> players)
    {
        int roomId = Interlocked.Increment(ref _nextRoomId);
        int locationId = players[0].LocationId;

        var state = new BattleState
        {
            LocationId = locationId,
            GameMode = assets.Locations.GetGameMode(locationId),
            Map = assets.MapForLocation(locationId),
        };

        int mode = state.ModeVariation;

        bool sharedSpawns = state.Map is null || state.Map.EnemySpawns.Count == 0;

        int slot = 1;
        var inTeam = new int[16];
        foreach (var player in players)
        {
            int team = TeamFor(slot, mode);
            int spawnIndex = sharedSpawns ? slot - 1 : inTeam[team & 15]++;
            state.Heroes.Add(CreateHero(state, slot, team, spawnIndex, player.BrawlerId, player.SkinId, player.PowerLevel, player.Name, player.LowId));
            slot++;
        }

        int seats = GameModes.PlayersPerMatch(mode, options.PlayersPerMatch);
        while (state.Heroes.Count < seats)
        {
            int botBrawler = assets.Characters.GetPlayableBrawlers() is { Count: > 0 } list
                ? list[Random.Shared.Next(list.Count)]
                : 0;
            int team = TeamFor(slot, mode);
            int spawnIndex = sharedSpawns ? slot - 1 : inTeam[team & 15]++;
            state.Heroes.Add(CreateHero(state, slot, team, spawnIndex, botBrawler, 0, BotPowerLevel, $"Bot {slot}", ownerLowId: 0));
            slot++;
        }

        if (GameModes.NoRespawn(mode) && state.Map is { } wilds)
        {
            state.CubeCsvId = assets.Items.PowerCube;
            int lootBox = options.ShowdownBoxes ? assets.Characters.RowOfName("LootBox") : -1;
            if (lootBox >= 0)
                foreach (var spot in wilds.Markers('4'))
                {
                    int hp = Math.Clamp(assets.Characters.GetHitpoints(lootBox), 500, 8191);
                    state.Structures.Add(new BattleStructure
                    {
                        Id = state.NextItemId++ & 0x3FFF,
                        CsvId = lootBox,
                        Team = 15,
                        X = spot.X,
                        Y = spot.Y,
                        MaxHitpoints = hp,
                        Hitpoints = hp,
                        Drops = 1,
                        Hidden = !assets.Characters.UsesShortBattleForm(lootBox),
                    });
                }
        }

        if (mode == GameModes.AttackDefend && state.Map is { } vault)
        {
            for (char marker = '6'; marker <= '9'; marker++)
            {
                if (vault.MarkerCentre(marker) is not { } spot) continue;
                int csvId = assets.Characters.SafeRow(marker - '6');
                if (csvId < 0) break;

                int hp = Math.Clamp(assets.Characters.GetHitpoints(csvId), 1000, 8191);
                state.Structures.Add(new BattleStructure
                {
                    Id = state.NextItemId++,
                    CsvId = csvId,
                    Team = spot.Y > vault.Height / 2 ? 0 : 1,
                    X = spot.X,
                    Y = spot.Y,
                    MaxHitpoints = hp,
                    Hitpoints = hp,
                    Hidden = !assets.Characters.UsesShortBattleForm(csvId),
                });
                break;
            }
        }

        if (mode == GameModes.LaserBall && state.Map is { } pitch)
        {
            state.Ball = new BattleBall { X = pitch.Width / 2, Y = pitch.Height / 2 };
            var top = pitch.MarkerCentre('7');
            var bottom = pitch.MarkerCentre('6');
            state.GoalX[0] = top?.X ?? pitch.Width / 2;
            state.GoalY[0] = top?.Y ?? 150;
            state.GoalX[1] = bottom?.X ?? pitch.Width / 2;
            state.GoalY[1] = bottom?.Y ?? pitch.Height - 150;
        }

        if (mode == GameModes.CoinRush && assets.Items.GemSpawner >= 0 && state.Map is { } arena)
        {
            state.GemCsvId = assets.Items.Gem;
            state.Items.Add(new BattleItem
            {
                Id = state.NextItemId++,
                CsvId = assets.Items.GemSpawner,
                IsSpawner = true,
                X = arena.Width / 2,
                Y = arena.Height / 2,
            });
        }

        var room = new BattleRoom(roomId, state, Math.Min(options.MaxTicks, GameModes.BattleTicks(mode)));
        foreach (var player in players)
        {
            LeaveRoom(player.LowId);
            room.AddClient(player.Client);
            _roomByPlayer[player.LowId] = room;
        }
        _rooms[roomId] = room;
        return room;
    }

    private const int BotPowerLevel = 25;

    private BattleHero CreateHero(BattleState state, int slot, int team, int spawnIndex, int brawlerId, int skinId, int powerLevel, string name, int ownerLowId)
    {
        int hp = ScaleHitpoints(Math.Max(1200, assets.Characters.GetHitpoints(brawlerId)), powerLevel);
        var weapon = assets.Weapons.ForBrawler(brawlerId);
        var ulti = assets.Weapons.UltiForBrawler(brawlerId);
        int maxAmmo = weapon.MaxCharge * 1000;
        var hero = new BattleHero
        {
            Slot = slot,
            IndexInTeam = spawnIndex,
            BrawlerId = brawlerId,
            SkinId = skinId,
            Team = team,
            Name = name,
            OwnerLowId = ownerLowId,
            MaxHitpoints = hp,
            Hitpoints = hp,
            BaseHitpoints = hp,
            SpeedPerSecond = assets.Characters.GetSpeed(brawlerId),
            PowerLevel = powerLevel,
            Weapon = weapon,
            Ulti = ulti,
            WeaponRow = assets.Weapons.WeaponRow(brawlerId),
            UltiRow = assets.Weapons.UltiRow(brawlerId),
            BurstStats = weapon,
            UltiChargeMultiplier = assets.Characters.GetUltiChargeMultiplier(brawlerId),
            WeaponRange = weapon.Range,
            WeaponCooldownTicks = Math.Max(1, (weapon.ActiveTimeMs + weapon.CooldownMs) / 50),
            VolleyIntervalTicks = Math.Max(1, weapon.MsBetweenAttacks / 50),
            AmmoPerTick = Math.Max(1, 1000 * 50 / Math.Max(1, weapon.RechargeMs)),
            MaxAmmo = maxAmmo,
        };

        var (x, y) = BattleRoom.SpawnFor(state, team, spawnIndex);
        hero.Transform.X = x;
        hero.Transform.Y = y;
        hero.Transform.Index = slot;
        hero.TeamRotation = state.Map?.SpawnRotation(team) ?? BattleRoom.SpawnRotation(slot);
        hero.EnemyRotation = hero.TeamRotation;
        hero.PlayingAnimation = true;
        foreach (var skill in hero.Skills) skill.Ammo = maxAmmo;
        return hero;
    }

    private static int ScaleHitpoints(int baseHitpoints, int powerLevel) =>
        baseHitpoints + baseHitpoints * Math.Clamp(powerLevel, 0, 50) * 4 / 500;

    private int TeamFor(int slot, int mode) => mode switch
    {
        GameModes.BattleRoyale => slot - 1,
        GameModes.BattleRoyaleTeam => (slot - 1) / 2,
        GameModes.Survival or GameModes.BossFight => 0,
        _ => slot <= GameModes.PlayersPerMatch(mode, options.PlayersPerMatch) / 2 ? 0 : 1,
    };

    public void Release(BattleRoom room)
    {
        _rooms.TryRemove(room.Id, out _);
        foreach (var (lowId, occupied) in _roomByPlayer)
            if (occupied.Id == room.Id) _roomByPlayer.TryRemove(lowId, out _);
    }
}
