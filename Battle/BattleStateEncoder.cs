using GameLogic.Battle;
using GameLogic.Csv;

namespace Battle;

public static class BattleStateEncoder
{
    private const int CharactersClassId = 16;
    private const int ObjectClassId = 2;
    private const int ProjectilesClassId = 6;
    private const int ProjectileObjectClassId = 1;
    private const int ItemsClassId = 18;
    private const int ItemObjectClassId = 4;
    private const int AreaEffectsClassId = 17;
    private const int AreaEffectObjectClassId = 3;

    private const int BushRevealRadius = 600;
    private const int LaserBallCsvId = 31;
    private const int LaserBallIndex = 103;

    private static List<T> Take<T>(List<T> source, ref int budget)
    {
        if (budget <= 0) { budget = 0; return []; }
        int count = Math.Min(source.Count, budget);
        budget -= count;
        return source.GetRange(0, count);
    }

    private static int NeutralIndexFor(BattleState state)
    {
        bool solo = state.ModeVariation is GameModes.BattleRoyale or GameModes.BattleRoyaleTeam;
        return solo ? SoloNeutral | (SoloNeutral << 4) : 103;
    }

    private const int SoloNeutral = 10;

    private const int ObjectGlobalIdBase = 2_000_000;
    private const int MaxObjects = 127;

    public static byte[] Encode(BattleState state, int viewerLowId)
    {
        var s = new BitStreamWriter();
        var heroes = state.Heroes;
        int mode = state.ModeVariation;
        int viewerSlot = state.HeroOf(viewerLowId)?.Slot ?? 1;
        state.ViewerSlot = viewerSlot;

        s.WritePositiveInt(ObjectGlobalIdBase + viewerSlot - 1, 21);
        s.WritePositiveInt(Math.Clamp(state.TicksLeft, 0, 32767), 15);
        s.WriteBoolean(state.GameOver);
        s.WriteBoolean(true);
        s.WriteBoolean(true);
        s.WriteBoolean(true);
        s.WriteBoolean(true);

        WriteDestroyedWalls(s, state);

        foreach (var hero in heroes)
        {
            s.WriteBoolean(true);
            s.WriteBoolean(true);
            if (hero.Slot == viewerSlot)
                s.WritePositiveInt(Math.Clamp(hero.UltiCharge, 0, BattleHero.FullUltiCharge), 12);
        }

        WriteModeState(s, state, mode);

        foreach (var hero in heroes)
        {
            s.WritePositiveInt(Math.Clamp(hero.Score, 0, 127), 7);

            int events = Math.Min(hero.KillFeed.Count, 15);
            s.WritePositiveInt(events, 4);
            for (int i = 0; i < events; i++)
            {
                s.WritePositiveInt(Math.Clamp(hero.KillFeed[i].VictimIndex, 0, 15), 4);
                s.WritePositiveInt(Math.Clamp(hero.KillFeed[i].Stars, 0, 7), 3);
            }
        }

        var visible = heroes.Where(h => h.Alive).ToList();
        bool withBall = mode == GameModes.LaserBall && state.Ball is not null;
        int ballId = heroes.Count;

        int budget = MaxObjects - visible.Count - (withBall ? 1 : 0);
        var vaults = Take(state.Structures.Where(v => v.Alive && !v.Hidden).ToList(), ref budget);
        var items = Take(state.Items, ref budget);
        var areas = Take(state.AreaEffects, ref budget);
        var shots = Take(state.Projectiles, ref budget);

        s.WritePositiveInt(visible.Count + shots.Count + items.Count + areas.Count + vaults.Count + (withBall ? 1 : 0), 7);

        foreach (var hero in visible)
        {
            s.WritePositiveInt(CharactersClassId, 5);
            s.WritePositiveInt(Math.Clamp(hero.BrawlerId, 0, 255), 8);
        }
        foreach (var shot in shots)
        {
            s.WritePositiveInt(ProjectilesClassId, 5);
            s.WritePositiveInt(Math.Clamp(shot.CsvId, 0, 255), 8);
        }
        foreach (var item in items)
        {
            s.WritePositiveInt(ItemsClassId, 5);
            s.WritePositiveInt(Math.Clamp(item.CsvId, 0, 255), 8);
        }
        foreach (var area in areas)
        {
            s.WritePositiveInt(AreaEffectsClassId, 5);
            s.WritePositiveInt(Math.Clamp(area.CsvId, 0, 255), 8);
        }
        foreach (var vault in vaults)
        {
            s.WritePositiveInt(CharactersClassId, 5);
            s.WritePositiveInt(Math.Clamp(vault.CsvId, 0, 255), 8);
        }
        if (withBall)
        {
            s.WritePositiveInt(CharactersClassId, 5);
            s.WritePositiveInt(LaserBallCsvId, 8);
        }

        foreach (var hero in visible)
        {
            s.WritePositiveInt(ObjectClassId, 5);
            s.WritePositiveInt(hero.Slot - 1, 14);
        }
        foreach (var shot in shots)
        {
            s.WritePositiveInt(ProjectileObjectClassId, 5);
            s.WritePositiveInt(shot.Id & 0x3FFF, 14);
        }
        foreach (var item in items)
        {
            s.WritePositiveInt(ItemObjectClassId, 5);
            s.WritePositiveInt(item.Id & 0x3FFF, 14);
        }
        foreach (var area in areas)
        {
            s.WritePositiveInt(AreaEffectObjectClassId, 5);
            s.WritePositiveInt(area.Id & 0x3FFF, 14);
        }
        foreach (var vault in vaults)
        {
            s.WritePositiveInt(ObjectClassId, 5);
            s.WritePositiveInt((ballId + 1 + vault.Id) & 0x3FFF, 14);
        }
        if (withBall)
        {
            s.WritePositiveInt(ObjectClassId, 5);
            s.WritePositiveInt(ballId, 14);
        }

        var viewer = heroes.FirstOrDefault(h => h.Slot == viewerSlot);
        foreach (var hero in visible)
            EncodeHero(s, state, hero, viewerSlot, Concealed(state, hero, viewer));

        foreach (var shot in shots)
            EncodeProjectile(s, state, shot);

        foreach (var item in items)
            EncodeItem(s, state, item);

        foreach (var area in areas)
            EncodeAreaEffect(s, state, area);

        foreach (var vault in vaults)
            EncodeStructure(s, state, vault);

        if (withBall) EncodeBall(s, state);
        return s.ToArray();
    }

    private static void WriteDestroyedWalls(BitStreamWriter s, BattleState state)
    {
        int xBits = state.IsLargeMap ? 6 : 5;
        int yBits = state.IsLargeMap ? 7 : 6;

        if (state.Map is not { } map || state.IsLargeMap)
        {
            s.WritePositiveInt(1, xBits);
            s.WritePositiveInt(0, yBits);
            s.WritePositiveInt(0, xBits);
            s.WritePositiveInt(0, yBits);
            return;
        }

        int lastColumn = Math.Min(map.Columns - 1, (1 << xBits) - 1);
        int lastRow = Math.Min(map.Rows - 1, (1 << yBits) - 1);

        s.WritePositiveInt(0, xBits);
        s.WritePositiveInt(0, yBits);
        s.WritePositiveInt(lastColumn, xBits);
        s.WritePositiveInt(lastRow, yBits);

        for (int column = 0; column <= lastColumn; column++)
            for (int row = 0; row <= lastRow; row++)
                if (map.IsDestructibleTile(column, row))
                    s.WriteBoolean(state.TileDestroyed(column, row));
    }

    private static void WriteModeState(BitStreamWriter s, BattleState state, int mode)
    {
        switch (mode)
        {
            case GameModes.BattleRoyale:
                s.WritePositiveInt(Math.Clamp(state.Heroes.Count(h => h.Alive), 0, 15), 4);
                break;
            case GameModes.BattleRoyaleTeam:
                int teamsAlive = state.Heroes.Where(h => h.Alive).Select(h => h.Team).Distinct().Count();
                s.WritePositiveInt(Math.Clamp(teamsAlive, 0, 7), 3);
                break;
        }

        if (mode == GameModes.LaserBall)
        {
            s.WriteBoolean(true);
            s.WriteSigned(state.GoalSignal, 1);
        }

        switch (mode)
        {
            case GameModes.AttackDefend:
                s.WritePositiveInt(SafePercent(state, 0), 7);
                s.WritePositiveInt(SafePercent(state, 1), 7);
                break;
            case GameModes.BossFight:
                s.WritePositiveInt(Math.Clamp(state.ObjectiveHealth[0], 0, 127), 7);
                break;
            case GameModes.BountyHunter:
                s.WritePositiveInt(Math.Clamp(state.TeamScores[0], 0, 7), 3);
                s.WritePositiveInt(Math.Clamp(state.TeamScores[1], 0, 7), 3);
                break;
            case GameModes.Survival:
                s.WritePositiveInt(0, 7);
                s.WritePositiveInt(0, 7);
                s.WriteBoolean(false);
                break;
        }
    }

    private static int SafePercent(BattleState state, int team)
    {
        var vault = state.Structures.FirstOrDefault(v => v.Team == team);
        return vault is null ? 100 : Math.Clamp(vault.HealthPercent, 0, 100);
    }

    private static int HitpointBits(int mode) => mode switch
    {
        GameModes.BattleRoyale or GameModes.BattleRoyaleTeam => 15,
        GameModes.BossFight => 17,
        _ => 13,
    };

    private static void EncodeProjectile(BitStreamWriter s, BattleState state, BattleProjectile shot)
    {
        var owner = state.Heroes.FirstOrDefault(h => h.Slot == shot.OwnerSlot);
        int shotIndex = owner is null ? NeutralIndexFor(state) : ObjectIndexFor(owner, state.Heroes.FirstOrDefault(h => h.Slot == state.ViewerSlot));
        WritePosition(s, state, shot.X, shot.Y, shotIndex, shot.Z);

        s.WritePositiveInt(Math.Clamp(shot.State, 0, 7), 3);
        if (shot.State == 4)
        {
            s.WriteBoolean(false);
            s.WritePositiveInt(0, state.IsLargeMap ? 14 : 10);
        }
        else if (shot.IsBouncing)
        {
            s.WriteBoolean(false);
        }
        s.WriteBoolean(false);

        if (shot.HasTriggerDelay || shot.HasPreExplosion) s.WritePositiveInt(0, 14);
        if (shot.HasPreExplosion) WriteMapCoordinates(s, state, shot.X, shot.Y);

        s.WritePositiveInt(Math.Clamp(shot.DirectionDegrees, 0, 1023), 10);
        s.WriteBoolean(shot.IsIndirect);
        if (shot.IsIndirect) WriteMapCoordinates(s, state, shot.TargetX, shot.TargetY);
    }

    private static void EncodeStructure(BitStreamWriter s, BattleState state, BattleStructure vault)
    {
        WriteObjectHeader(s, state, vault.X, vault.Y, NeutralIndexFor(state), 0, 10);

        int hpBits = HitpointBits(state.ModeVariation);
        int hpMax = (1 << hpBits) - 1;
        s.WritePositiveInt(0, 3);
        s.WritePositiveInt(Math.Clamp(vault.Hitpoints, 0, hpMax), hpBits);
        s.WritePositiveInt(Math.Clamp(vault.MaxHitpoints, 1, hpMax), hpBits);
        s.WritePositiveInt(0, 1);
        s.WritePositiveInt(0, 1);
        s.WritePositiveInt(0, 9);
        s.WritePositiveInt(0, 5);
    }

    private static void EncodeAreaEffect(BitStreamWriter s, BattleState state, BattleAreaEffect area)
    {
        var caster = state.Heroes.FirstOrDefault(h => h.Slot == area.OwnerSlot);
        int areaIndex = caster is null ? NeutralIndexFor(state) : ObjectIndexFor(caster, state.Heroes.FirstOrDefault(h => h.Slot == state.ViewerSlot));
        WriteObjectHeader(s, state, area.X, area.Y, areaIndex, 0, 10);
        if (area.Type == AreaEffectTypes.DelayedDamage) s.WriteBoolean(false);
    }

    private static void EncodeItem(BitStreamWriter s, BattleState state, BattleItem item)
    {
        WriteObjectHeader(s, state, item.X, item.Y, NeutralIndexFor(state), 0, 10);

        int kind = item.CsvId;
        if (kind == 5 || kind == 7 || kind == 8 || kind == 11 || kind == 13)
        {
            s.WritePositiveInt(Math.Clamp(item.X, 0, 16383), 14);
            s.WritePositiveInt(Math.Clamp(item.Y, 0, 16383), 14);
        }
        else if (kind == 6 || kind == 12)
        {
            s.WritePositiveInt(Math.Clamp(item.X, 0, 2047), 11);
            s.WritePositiveInt(Math.Clamp(item.Y, 0, 2047), 11);
        }
    }

    private static void WriteMapCoordinates(BitStreamWriter s, BattleState state, int x, int y) =>
        WritePair(s, state.IsLargeMap, x, y);

    private static void WriteCoordinates(BitStreamWriter s, BattleState state, int x, int y)
    {
        int mode = state.ModeVariation;
        WritePair(s, mode is GameModes.BattleRoyale or GameModes.BattleRoyaleTeam || state.IsLargeMap, x, y);
    }

    private static void WritePair(BitStreamWriter s, bool wide, int x, int y)
    {
        if (wide)
        {
            s.WritePositiveInt(Math.Clamp(x, 0, 32767), 15);
            s.WritePositiveInt(Math.Clamp(y, 0, 65535), 16);
        }
        else
        {
            s.WritePositiveInt(Math.Clamp(x, 0, 8191), 13);
            s.WritePositiveInt(Math.Clamp(y, 0, 16383), 14);
        }
    }

    private static void WritePosition(BitStreamWriter s, BattleState state, int x, int y, int index, int z)
    {
        int mode = state.ModeVariation;
        int indexBits = mode is GameModes.BattleRoyale or GameModes.BattleRoyaleTeam ? 8 : 7;
        WriteCoordinates(s, state, x, y);
        s.WritePositiveInt(Math.Clamp(index, 0, (1 << indexBits) - 1), indexBits);
        s.WritePositiveInt(z, 12);
    }

    private static void WriteObjectHeader(BitStreamWriter s, BattleState state, int x, int y, int index, int z, int visibility)
    {
        WritePosition(s, state, x, y, index, z);
        s.WritePositiveInt(visibility, 4);
    }

    private static int ObjectIndexFor(BattleHero hero, BattleHero? viewer)
    {
        int side = viewer is null || hero.Team == viewer.Team ? 0 : 1;
        return (hero.Slot - 1) | (side << 4);
    }

    private static bool Concealed(BattleState state, BattleHero hero, BattleHero? viewer)
    {
        if (state.Map is not { } map || viewer is null) return false;
        if (viewer.Team == hero.Team || hero.Slot == viewer.Slot) return false;
        if (!map.HidesHero(hero.Transform.X, hero.Transform.Y)) return false;

        long dx = viewer.Transform.X - hero.Transform.X;
        long dy = viewer.Transform.Y - hero.Transform.Y;
        return dx * dx + dy * dy > (long)BushRevealRadius * BushRevealRadius;
    }

    private static void EncodeHero(BitStreamWriter s, BattleState state, BattleHero hero, int viewerSlot, bool concealed)
    {
        int mode = state.ModeVariation;
        bool isOwn = hero.Slot == viewerSlot;
        var viewer = state.Heroes.FirstOrDefault(h => h.Slot == viewerSlot);

        WriteObjectHeader(s, state, hero.Transform.X, hero.Transform.Y, ObjectIndexFor(hero, viewer), 0, 10);

        if (isOwn)
        {
            s.WriteBoolean(false);
        }
        else
        {
            s.WritePositiveInt(Math.Clamp(hero.TeamRotation, 0, 511), 9);
            s.WritePositiveInt(Math.Clamp(hero.EnemyRotation, 0, 511), 9);
        }

        s.WritePositiveInt(Math.Clamp(hero.State, 0, 7), 3);
        s.WriteBoolean(hero.Slowed);
        s.WriteBoolean(false);
        s.WriteSigned(hero.PlayingAnimation ? Math.Clamp(hero.PlayedAnimation, 0, 63) : 0, 6);
        s.WriteBoolean(false);
        s.WriteBoolean(hero.Stunned);
        s.WriteBoolean(hero.Poisoned);
        s.WritePositiveInt(0, 2);
        s.WritePositiveInt(0, 7);
        s.WritePositiveInt(0, 5);

        int hpBits = HitpointBits(mode);
        int hpMax = (1 << hpBits) - 1;
        s.WritePositiveInt(Math.Clamp(hero.Hitpoints, 0, hpMax), hpBits);
        s.WritePositiveInt(Math.Clamp(hero.MaxHitpoints, 0, hpMax), hpBits);

        if (mode == GameModes.BountyHunter)
        {
            s.WritePositiveInt(Math.Clamp(hero.ItemsCarried, 0, 127), 7);
        }
        else
        {
            bool solo = mode is GameModes.BattleRoyale or GameModes.BattleRoyaleTeam;
            s.WritePositiveInt(Math.Clamp(solo ? hero.PowerCubes : hero.ItemsCarried, 0, 63), 6);
            if (mode == GameModes.LaserBall) s.WriteBoolean(hero.HasBall);
        }

        s.WritePositiveInt(0, 13);
        s.WritePositiveInt(0, 11);
        s.WriteBoolean(false);
        s.WriteBoolean(hero.ImmunityShield);
        s.WriteBoolean(false);
        s.WriteBoolean(hero.Rage);
        s.WriteBoolean(hero.UltiAiming);
        s.WriteBoolean(hero.UltiActive || hero.UltiActiveTicks > 0);
        s.WriteBoolean(hero.Invisible || concealed);
        s.WriteBoolean(false);
        s.WriteBoolean(hero.NotFullyVisible || concealed);
        s.WritePositiveInt(0, 9);

        if (isOwn) s.WriteSigned(0, 9);

        s.WritePositiveInt(0, 5);

        foreach (var skill in hero.Skills)
        {
            s.WritePositiveInt(Math.Clamp(skill.ActiveTicks, 0, 2047), 11);
            s.WriteBoolean(skill.Active);
            s.WritePositiveInt(Math.Clamp(skill.Unknown, 0, 4095), 12);
            if (skill.Name == "Weapon") s.WritePositiveInt(Math.Clamp(skill.Ammo, 0, 4095), 12);
        }
    }

    private static void EncodeBall(BitStreamWriter s, BattleState state)
    {
        int x = state.Ball?.X ?? (state.Map is { } map ? map.Width / 2 : 2550);
        int y = state.Ball?.Y ?? (state.Map is { } other ? other.Height / 2 : 4950);

        int index = LaserBallIndex;
        if (state.Ball is { CarrierSlot: > 0 } ball)
        {
            var carrier = state.Heroes.FirstOrDefault(h => h.Slot == ball.CarrierSlot);
            if (carrier is not null)
                index = ObjectIndexFor(carrier, state.Heroes.FirstOrDefault(h => h.Slot == state.ViewerSlot));
        }

        WriteObjectHeader(s, state, x, y, index, 0, 10);

        int hpBits = HitpointBits(state.ModeVariation);
        s.WritePositiveInt(0, 3);
        s.WritePositiveInt(1, hpBits);
        s.WritePositiveInt(1, hpBits);
        s.WritePositiveInt(0, 2);
        s.WritePositiveInt(0, 2);
        s.WritePositiveInt(0, 1);
        s.WritePositiveInt(0, 1);
        s.WritePositiveInt(0, 9);
        s.WritePositiveInt(0, 5);
    }
}
