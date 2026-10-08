using System.Collections.Concurrent;
using GameLogic.Battle;
using GameLogic.Csv;

namespace Battle;

public readonly record struct BattleInput(int LowId, int Type, int X, int Y);

public interface IBattleClient
{
    int LowId { get; }
    Task SendVisionUpdateAsync(int ticks, byte[] state, CancellationToken ct);
    Task SendBattleOverAsync(BattleOutcome outcome, CancellationToken ct);
}

public sealed record BattleParticipant(string Name, int BrawlerId, int SkinId, int Team, int Kills, int PowerLevel, bool IsPlayer);

public sealed record BattleOutcome(
    int LowId,
    int BrawlerId,
    int Rank,
    int Kills,
    bool Victory,
    string GameMode,
    IReadOnlyList<BattleParticipant> Participants);

public sealed class BattleRoom
{
    private const int TickMilliseconds = 50;
    private const int TicksPerSecond = 1000 / TickMilliseconds;

    private const int AttackAnimationTicks = 4;
    private const int RespawnTicks = 60;

    private const int MaxProjectiles = 40;
    private const int ShotLogLimit = 4;
    private const int IndirectBlastRadius = 400;
    private const int BlastLingerTicks = 3;
    private const int ThrowArcHeight = 400;
    private const int DotIntervalTicks = 10;
    private const int GemsToWin = 10;
    private const int ScoreCountdownTicks = 300;
    private const int DashHitRadius = 320;
    private const int BallPickupRadius = 320;
    private const int GoalRadius = 900;
    private const int BallKickRange = 2200;
    private const int BallSpeed = 2300;
    private const int GoalsToWin = 2;
    private const int DetourTicks = 14;
    private const int BallLooseTicks = 12;
    private const int GoalCelebrationTicks = 30;
    private const int KickOffFreezeTicks = 20;
    private const int GemSpawnTicks = 100;
    private const int MaxLooseGems = 12;
    private const int MaxLooseItems = 40;
    private const int PickupRadius = 300;
    private const int IntroTicks = 80;
    private const int BotStandoff = 1800;

    private const int InputLogPerType = 3;
    private const int BotFirePeriod = 40;
    private const int BotUltiChargeRate = 55;
    private const int MineSpread = 260;
    private const int MineBlastRadius = 600;
    private const int MineTriggerRadius = 340;

    private readonly ConcurrentQueue<BattleInput> _inputs = new();
    private readonly List<IBattleClient> _clients = [];
    private readonly object _clientsLock = new();
    private readonly int _maxTicks;
    private readonly Dictionary<int, int> _loggedInputs = [];
    private int _loggedShots;
    private int _countdown;
    private bool _everHadClients;

    public int Id { get; }
    public BattleState State { get; }

    public BattleRoom(int id, BattleState state, int maxTicks = 2400)
    {
        Id = id;
        State = state;
        _maxTicks = maxTicks;
    }

    public void AddClient(IBattleClient client)
    {
        lock (_clientsLock)
        {
            _clients.Add(client);
            _everHadClients = true;
        }
    }

    private bool Abandoned()
    {
        lock (_clientsLock) return _everHadClients && _clients.Count == 0;
    }

    public void RemoveClient(int lowId)
    {
        lock (_clientsLock) _clients.RemoveAll(c => c.LowId == lowId);
    }

    public void EnqueueInput(BattleInput input) => _inputs.Enqueue(input);

    public async Task RunAsync(CancellationToken ct)
    {
        var map = State.Map;
        Console.WriteLine($"[battle {Id}] старт: режим {State.GameMode}, карта {map?.Name ?? "без разметки"} " +
                          $"{map?.Columns ?? 0}x{map?.Rows ?? 0}, бойцов {State.Heroes.Count}, " +
                          (GameModes.IsSolo(State.ModeVariation)
                              ? "каждый сам за себя, "
                              : $"команд {State.Heroes.Select(h => h.Team).Distinct().Count()}, ") +
                          $"ящиков {State.Structures.Count}, длина состояния " +
                          $"{BattleStateEncoder.Encode(State, State.Heroes.Count > 0 ? State.Heroes[0].OwnerLowId : 0).Length} б");
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMilliseconds));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                State.Ticks++;
                State.TicksLeft = 0;
                ApplyInputs();
                Simulate();
                await BroadcastAsync(ct);
                foreach (var hero in State.Heroes) hero.KillFeed.Clear();

                if (State.GameOver || State.Ticks >= _maxTicks || Abandoned()) break;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            timer.Dispose();
        }

        State.GameOver = true;
        await SendOutcomesAsync(ct);
        Console.WriteLine($"[battle {Id}] окончен на тике {State.Ticks}");
    }

    private void ApplyInputs()
    {
        while (_inputs.TryDequeue(out var input))
        {
            var hero = State.HeroOf(input.LowId);
            if (hero is null) continue;

            _loggedInputs.TryGetValue(input.Type, out int seen);
            if (seen < InputLogPerType)
            {
                _loggedInputs[input.Type] = seen + 1;
                Console.WriteLine($"[battle {Id}] ввод от {input.LowId}: тип {input.Type}, точка {input.X},{input.Y}");
            }

            if (!hero.Alive) continue;

            switch (input.Type)
            {
                case 100:
                    if (hero.FreezeInputTicks <= 0) PlaceAt(hero, input.X, input.Y);
                    break;
                case 107:
                    hero.UltiAiming = true;
                    break;
                case 108:
                    hero.UltiAiming = false;
                    break;
                default:
                    if (input.Type is >= 0 and < 100) UseSkill(hero, input.Type, input.X, input.Y);
                    break;
            }
        }
    }

    private void PlaceAt(BattleHero hero, int x, int y)
    {
        var map = State.Map;
        int nx = map?.ClampX(x) ?? Math.Clamp(x, 0, BattleMap.MaxEncodableX);
        int ny = map?.ClampY(y) ?? Math.Clamp(y, 0, BattleMap.MaxEncodableY);

        if (map is not null && State.BlocksMovement(nx, ny))
        {
            if (!State.BlocksMovement(nx, hero.Transform.Y)) { hero.Transform.X = nx; return; }
            if (!State.BlocksMovement(hero.Transform.X, ny)) { hero.Transform.Y = ny; return; }
            return;
        }

        hero.Transform.X = nx;
        hero.Transform.Y = ny;
    }

    private void UseSkill(BattleHero hero, int skillRow, int x, int y)
    {
        if (skillRow == hero.UltiRow && hero.UltiRow >= 0)
        {
            if (hero.UltiCharge < BattleHero.FullUltiCharge) return;
            hero.UltiCharge = 0;
            hero.UltiActiveTicks = Math.Max(AttackAnimationTicks, hero.Ulti.ActiveTimeMs / TickMilliseconds);
            Fire(hero, hero.Ulti, x, y, spendAmmo: false);
            return;
        }

        Fire(hero, hero.Weapon, x, y, spendAmmo: true);
    }

    private void FireWeapon(BattleHero shooter, int x, int y) => Fire(shooter, shooter.Weapon, x, y, spendAmmo: true);

    private void Fire(BattleHero shooter, WeaponStats stats, int x, int y, bool spendAmmo)
    {
        var weapon = shooter.Skills.First(s => s.Name == "Weapon");
        if (shooter.CooldownTicks > 0 || (spendAmmo && weapon.Ammo < 1000))
        {
            if (!shooter.IsBot && _loggedShots < ShotLogLimit)
            {
                _loggedShots++;
                Console.WriteLine($"[battle {Id}] выстрел не прошёл: откат {shooter.CooldownTicks}, патронов {weapon.Ammo}");
            }
            return;
        }

        if (spendAmmo) weapon.Ammo -= 1000;
        shooter.BurstStats = stats;
        shooter.WeaponRange = stats.Range;
        shooter.CooldownTicks = Math.Max(1, (stats.ActiveTimeMs + stats.CooldownMs) / TickMilliseconds);

        int aim = AngleTo(shooter.Transform.X, shooter.Transform.Y, x, y);
        shooter.TeamRotation = aim;
        shooter.EnemyRotation = aim;
        shooter.BurstAim = aim;
        shooter.BurstAimX = x;
        shooter.BurstAimY = y;
        shooter.BurstVolleysLeft = stats.Volleys;
        shooter.BurstTimer = 0;

        if (_loggedShots < ShotLogLimit)
        {
            _loggedShots++;
            Console.WriteLine($"[battle {Id}] {shooter.Name} бьёт {(spendAmmo ? "оружием" : "ультой")}: {stats.Volleys} по {stats.Bullets}, " +
                              $"снаряд {stats.Projectile?.Name ?? "нет"}, дальность {stats.Range}, " +
                              $"разброс {stats.SpreadDegrees}, патронов {weapon.Ammo}");
        }

        if (!spendAmmo) Summon(shooter, stats, x, y);
        ReleaseVolley(shooter);
    }

    private void Summon(BattleHero hero, WeaponStats stats, int x, int y)
    {
        int px = State.Map?.ClampX(x) ?? x;
        int py = State.Map?.ClampY(y) ?? y;

        if (stats.SummonedCharacter >= 0)
        {
            int hp = Math.Max(500, stats.SummonHitpoints);
            State.Structures.Add(new BattleStructure
            {
                Hidden = !stats.SummonUsesShortForm,
                Id = State.NextItemId++ & 0x3FFF,
                CsvId = stats.SummonedCharacter,
                OwnerSlot = hero.Slot,
                Team = hero.Team,
                X = px,
                Y = py,
                MaxHitpoints = hp,
                Hitpoints = hp,
            });
            Console.WriteLine($"[battle {Id}] {hero.Name} призвал существо {stats.SummonedCharacter}");
        }

        if (stats.SpawnedItem < 0) return;

        int count = Math.Max(1, stats.SpawnedItemCount);
        for (int i = 0; i < count; i++)
        {
            int angle = 360 * i / count;
            double radians = angle * Math.PI / 180;
            int bx = hero.Transform.X + (int)Math.Round(Math.Cos(radians) * MineSpread);
            int by = hero.Transform.Y + (int)Math.Round(Math.Sin(radians) * MineSpread);

            State.Items.Add(new BattleItem
            {
                Id = State.NextItemId++ & 0x3FFF,
                CsvId = stats.SpawnedItem,
                OwnerSlot = hero.Slot,
                Team = hero.Team,
                Damage = stats.SpawnedItemDamage,
                BlastRadius = stats.SpawnedItemBlast?.Radius > 0 ? stats.SpawnedItemBlast.Radius : MineBlastRadius,
                TriggerRadius = stats.SpawnedItemTrigger > 0 ? stats.SpawnedItemTrigger : MineTriggerRadius,
                Blast = stats.SpawnedItemBlast,
                X = State.Map?.ClampX(bx) ?? bx,
                Y = State.Map?.ClampY(by) ?? by,
            });
        }
        Console.WriteLine($"[battle {Id}] {hero.Name} оставил {count} шт предмета {stats.SpawnedItem} по {stats.SpawnedItemDamage} урона");
    }

    private void ReleaseVolley(BattleHero shooter)
    {
        if (shooter.BurstVolleysLeft <= 0) return;
        shooter.BurstVolleysLeft--;
        shooter.BurstTimer = shooter.VolleyIntervalTicks;
        shooter.State = 2;
        shooter.AttackTicks = AttackAnimationTicks;

        if (KickBall(shooter)) return;

        if (shooter.BurstStats.IsCharge)
        {
            StartDash(shooter);
            return;
        }

        var projectile = shooter.BurstStats.Projectile;
        if (projectile is null)
        {
            HitClosest(shooter, ScaleDamage(shooter, shooter.BurstStats.DamagePerBullet * shooter.BurstStats.Bullets));
            return;
        }

        int bullets = shooter.BurstStats.Bullets;
        int spread = shooter.BurstStats.SpreadDegrees;
        int damage = ScaleDamage(shooter, shooter.BurstStats.DamagePerBullet);
        int aim = shooter.BurstAim + VolleySweep(shooter, spread, bullets);

        int reach = shooter.WeaponRange;
        if (projectile.IsIndirect)
        {
            int dx = shooter.BurstAimX - shooter.Transform.X;
            int dy = shooter.BurstAimY - shooter.Transform.Y;
            reach = Math.Clamp((int)Math.Sqrt((long)dx * dx + (long)dy * dy), 300, shooter.WeaponRange);
        }

        for (int i = 0; i < bullets && State.Projectiles.Count < MaxProjectiles; i++)
        {
            int offset = bullets == 1 ? 0 : spread * (2 * i - (bullets - 1)) / (2 * (bullets - 1));
            int direction = ((aim + offset) % 360 + 360) % 360;
            double radians = direction * Math.PI / 180;

            State.Projectiles.Add(new BattleProjectile
            {
                Id = State.NextProjectileId++ & 0x3FFF,
                CsvId = projectile.CsvId,
                OwnerSlot = shooter.Slot,
                Team = shooter.Team,
                X = shooter.Transform.X,
                Y = shooter.Transform.Y,
                TargetX = shooter.Transform.X + (int)Math.Round(Math.Cos(radians) * reach),
                TargetY = shooter.Transform.Y + (int)Math.Round(Math.Sin(radians) * reach),
                DirectionDegrees = direction,
                StepPerTick = Math.Max(1, projectile.SpeedPerSecond / TicksPerSecond),
                RemainingRange = reach,
                LaunchRange = reach,
                Damage = damage,
                HitRadius = Math.Max(180, projectile.Radius + 140),
                BlastRadius = projectile.IsIndirect ? IndirectBlastRadius : 0,
                IsIndirect = projectile.IsIndirect,
                IsBouncing = projectile.IsBouncing,
                HasTriggerDelay = projectile.TriggerWithDelayMs > 0,
                HasPreExplosion = projectile.PreExplosionTimeMs > 0,
                AreaEffect = projectile.AreaEffect,
            });
        }
    }

    private static int VolleySweep(BattleHero shooter, int spread, int bullets)
    {
        if (spread == 0 || bullets > 1) return 0;

        int total = shooter.BurstStats.Volleys;
        if (total < 2) return 0;

        int fired = total - shooter.BurstVolleysLeft - 1;
        return spread * (2 * fired - (total - 1)) / (2 * (total - 1));
    }

    private void StartDash(BattleHero hero)
    {
        double radians = hero.BurstAim * Math.PI / 180;
        int reach = hero.BurstStats.Range;
        int damage = ScaleDamage(hero, hero.BurstStats.DamagePerBullet * hero.BurstStats.Bullets);

        if (!hero.BurstStats.IsInstantCharge)
        {
            int aimX = hero.Transform.X + (int)Math.Round(Math.Cos(radians) * reach);
            int aimY = hero.Transform.Y + (int)Math.Round(Math.Sin(radians) * reach);
            hero.DashTargetX = State.Map?.ClampX(aimX) ?? aimX;
            hero.DashTargetY = State.Map?.ClampY(aimY) ?? aimY;
            hero.DashStep = Math.Max(1, hero.BurstStats.ChargeSpeed / TicksPerSecond);
            hero.DashTicksLeft = Math.Max(1, reach / hero.DashStep);
            hero.DashDamage = damage;
            hero.DashHits.Clear();
            hero.FreezeInputTicks = Math.Max(hero.FreezeInputTicks, hero.DashTicksLeft);
            return;
        }

        int fromX = hero.Transform.X;
        int fromY = hero.Transform.Y;
        int steps = Math.Max(1, reach / (BattleMap.TileSize / 2));
        var struck = new HashSet<int>();
        int landedX = fromX;
        int landedY = fromY;

        for (int i = 1; i <= steps; i++)
        {
            int x = fromX + (int)Math.Round(Math.Cos(radians) * reach * i / steps);
            int y = fromY + (int)Math.Round(Math.Sin(radians) * reach * i / steps);
            x = State.Map?.ClampX(x) ?? x;
            y = State.Map?.ClampY(y) ?? y;

            if (State.BlocksMovement(x, y)) { Shatter(x, y); break; }

            landedX = x;
            landedY = y;
            StrikeAlongDash(hero, x, y, damage, struck);
        }

        hero.Transform.X = landedX;
        hero.Transform.Y = landedY;
    }

    private void AdvanceDash(BattleHero hero)
    {
        hero.DashTicksLeft--;

        int dx = hero.DashTargetX - hero.Transform.X;
        int dy = hero.DashTargetY - hero.Transform.Y;
        int left = (int)Math.Sqrt((long)dx * dx + (long)dy * dy);
        int step = Math.Min(hero.DashStep, left);

        if (step > 0 && left > 0)
        {
            int nx = hero.Transform.X + dx * step / left;
            int ny = hero.Transform.Y + dy * step / left;
            nx = State.Map?.ClampX(nx) ?? nx;
            ny = State.Map?.ClampY(ny) ?? ny;

            if (State.BlocksMovement(nx, ny))
            {
                Shatter(nx, ny);
                hero.DashTicksLeft = 0;
            }
            else
            {
                hero.Transform.X = nx;
                hero.Transform.Y = ny;
                hero.TeamRotation = AngleTo(nx, ny, hero.DashTargetX, hero.DashTargetY);
                hero.EnemyRotation = hero.TeamRotation;
            }
        }

        StrikeAlongDash(hero, hero.Transform.X, hero.Transform.Y, hero.DashDamage, hero.DashHits);
        if (left <= step) hero.DashTicksLeft = 0;
    }

    private void StrikeAlongDash(BattleHero hero, int x, int y, int damage, HashSet<int> struck)
    {
        foreach (var victim in State.Heroes)
        {
            if (!victim.Alive || victim.Team == hero.Team || !struck.Add(victim.Slot)) continue;
            if (Span(victim, x, y) > (long)DashHitRadius * DashHitRadius)
            {
                struck.Remove(victim.Slot);
                continue;
            }
            Damage(hero, victim, damage);
        }
    }

    private static int ScaleDamage(BattleHero shooter, int baseDamage) =>
        baseDamage
        + baseDamage * Math.Clamp(shooter.PowerLevel, 0, 50) * 4 / 500
        + baseDamage * Math.Clamp(shooter.PowerCubes, 0, 20) / 10;

    private void HitClosest(BattleHero shooter, int damage)
    {
        var target = State.Heroes
            .Where(h => h.Team != shooter.Team && h.Alive)
            .OrderBy(h => DistanceSquared(shooter, h))
            .FirstOrDefault();
        if (target is null) return;

        long range = (long)shooter.WeaponRange * shooter.WeaponRange;
        if (DistanceSquared(shooter, target) > range) return;
        if (!HasLineOfSight(shooter.Transform.X, shooter.Transform.Y, target.Transform.X, target.Transform.Y)) return;
        Damage(shooter, target, damage);
    }

    public bool HasLineOfSight(int fromX, int fromY, int toX, int toY)
    {
        if (State.Map is not { } map) return true;

        int dx = toX - fromX;
        int dy = toY - fromY;
        int distance = (int)Math.Sqrt((long)dx * dx + (long)dy * dy);
        int steps = Math.Max(1, distance / (BattleMap.TileSize / 2));

        for (int i = 1; i <= steps; i++)
        {
            int x = fromX + dx * i / steps;
            int y = fromY + dy * i / steps;
            if (State.BlocksProjectiles(map.ClampX(x), map.ClampY(y))) return false;
        }
        return true;
    }

    private void DamageStructures(BattleHero shooter, int x, int y, int radius, int amount)
    {
        foreach (var vault in State.Structures)
        {
            if (!vault.Alive || vault.Team == shooter.Team) continue;
            long dx = vault.X - x;
            long dy = vault.Y - y;
            if (dx * dx + dy * dy > (long)radius * radius) continue;

            vault.Hitpoints = Math.Max(0, vault.Hitpoints - amount);
            if (vault.Alive || vault.Drops <= 0) continue;

            for (int i = 0; i < vault.Drops; i++) DropCube(vault.X, vault.Y);
            Console.WriteLine($"[battle {Id}] {shooter.Name} разбил ящик, выпало {vault.Drops}");
        }
    }

    private void DropCube(int x, int y)
    {
        if (State.CubeCsvId < 0) return;
        int angle = Random.Shared.Next(360);
        int distance = Random.Shared.Next(60, 170);
        double radians = angle * Math.PI / 180;
        int cx = x + (int)Math.Round(Math.Cos(radians) * distance);
        int cy = y + (int)Math.Round(Math.Sin(radians) * distance);

        State.Items.Add(new BattleItem
        {
            Id = State.NextItemId++ & 0x3FFF,
            CsvId = State.CubeCsvId,
            IsCollectable = true,
            IsPowerCube = true,
            X = State.Map?.ClampX(cx) ?? cx,
            Y = State.Map?.ClampY(cy) ?? cy,
        });
    }

    private void Damage(BattleHero shooter, BattleHero target, int amount)
    {
        target.Hitpoints = Math.Max(0, target.Hitpoints - amount);
        int gain = amount * shooter.UltiChargeMultiplier / 250;
        if (shooter.IsBot) gain = gain * BotUltiChargeRate / 100;
        shooter.UltiCharge = Math.Min(BattleHero.FullUltiCharge, shooter.UltiCharge + gain);

        if (target.Alive) return;

        shooter.Kills++;
        target.Deaths++;
        target.RespawnTicks = RespawnTicks;

        if (State.ModeVariation == GameModes.BountyHunter)
        {
            int stars = 1 + target.ItemsCarried;
            State.TeamScores[shooter.Team & 15] += stars;
            shooter.Score += stars;
            shooter.ItemsCarried += stars;
            shooter.KillFeed.Add(new KillFeedEntry(target.Slot - 1, Math.Clamp(stars, 0, 7), State.Ticks));
        }
        else
        {
            shooter.KillFeed.Add(new KillFeedEntry(target.Slot - 1, 0, State.Ticks));
        }

        if (State.ModeVariation == GameModes.CoinRush)
        {
            for (int i = 0; i < target.ItemsCarried; i++)
                DropGem(target.Transform.X, target.Transform.Y);
            target.Score = 0;
        }

        if (GameModes.NoRespawn(State.ModeVariation))
            for (int i = 0; i <= target.PowerCubes; i++)
                DropCube(target.Transform.X, target.Transform.Y);
        target.ItemsCarried = 0;
        Console.WriteLine($"[battle {Id}] {shooter.Name} убил {target.Name} на тике {State.Ticks}" +
                          (GameModes.NoRespawn(State.ModeVariation)
                              ? $", в живых {State.Heroes.Count(h => h.Alive)}"
                              : string.Empty));
    }

    private void MoveProjectiles()
    {
        for (int i = State.Projectiles.Count - 1; i >= 0; i--)
        {
            var p = State.Projectiles[i];
            int step = Math.Min(p.StepPerTick, p.RemainingRange);
            double radians = p.DirectionDegrees * Math.PI / 180;
            int nextX = p.X + (int)Math.Round(Math.Cos(radians) * step);
            int nextY = p.Y + (int)Math.Round(Math.Sin(radians) * step);

            p.X = State.Map?.ClampX(nextX) ?? Math.Clamp(nextX, 0, BattleMap.MaxEncodableX);
            p.Y = State.Map?.ClampY(nextY) ?? Math.Clamp(nextY, 0, BattleMap.MaxEncodableY);
            p.RemainingRange -= step;
            bool walled = !p.IsIndirect && State.BlocksProjectiles(p.X, p.Y);

            if (p.IsIndirect && p.LaunchRange > 0)
            {
                double flown = 1.0 - (double)p.RemainingRange / p.LaunchRange;
                double height = 1.0 - Math.Pow(2 * flown - 1, 2);
                p.Z = Math.Clamp((int)Math.Round(ThrowArcHeight * height), 0, 4095);
            }

            var shooter = State.Heroes.FirstOrDefault(h => h.Slot == p.OwnerSlot);
            bool blocked = nextX != p.X || nextY != p.Y || walled;
            if (walled) Shatter(p.X, p.Y);

            if (!p.IsIndirect)
            {
                var hit = State.Heroes.FirstOrDefault(h => h.Alive && h.Team != p.Team && Reaches(p, h));
                if (shooter is not null) DamageStructures(shooter, p.X, p.Y, p.HitRadius, p.Damage);

                if (hit is not null)
                {
                    if (shooter is not null) Damage(shooter, hit, p.Damage);
                    SpawnAreaEffect(p);
                    State.Projectiles.RemoveAt(i);
                    continue;
                }
                if (p.RemainingRange <= 0 || blocked)
                {
                    SpawnAreaEffect(p);
                    State.Projectiles.RemoveAt(i);
                }
                continue;
            }

            if (p.LingerTicks > 0)
            {
                if (--p.LingerTicks <= 0) State.Projectiles.RemoveAt(i);
                continue;
            }

            if (p.RemainingRange > 0 && !blocked) continue;

            if (shooter is not null)
            {
                int blast = p.AreaEffect?.Radius > 0 ? p.AreaEffect.Radius : p.BlastRadius;
                bool overTime = p.AreaEffect?.Type == AreaEffectTypes.Dot;
                foreach (var victim in State.Heroes)
                {
                    if (!victim.Alive || victim.Team == p.Team) continue;
                    long dx = victim.Transform.X - p.X;
                    long dy = victim.Transform.Y - p.Y;
                    if (dx * dx + dy * dy <= (long)blast * blast && !overTime)
                        Damage(shooter, victim, p.Damage);
                }
            }

            SpawnAreaEffect(p);
            p.State = 4;
            p.Z = 0;
            p.LingerTicks = BlastLingerTicks;
        }
    }

    private void SpawnAreaEffect(BattleProjectile p)
    {
        if (p.AreaEffect is not { } effect) return;

        int ticks = Math.Max(1, effect.TimeMs / TickMilliseconds);
        bool overTime = effect.Type == AreaEffectTypes.Dot;

        State.AreaEffects.Add(new BattleAreaEffect
        {
            Id = State.NextAreaEffectId++ & 0x3FFF,
            CsvId = effect.CsvId,
            OwnerSlot = p.OwnerSlot,
            Team = p.Team,
            X = p.X,
            Y = p.Y,
            Radius = effect.Radius > 0 ? effect.Radius : p.HitRadius,
            Type = effect.Type,
            TickDamage = overTime ? Math.Max(1, p.Damage / 5) : 0,
            TicksLeft = ticks,
        });
    }

    private void TickAreaEffects()
    {
        for (int i = State.AreaEffects.Count - 1; i >= 0; i--)
        {
            var effect = State.AreaEffects[i];
            if (--effect.TicksLeft <= 0) { State.AreaEffects.RemoveAt(i); continue; }
            if (effect.TickDamage <= 0) continue;
            if (--effect.DamageTimer > 0) continue;

            effect.DamageTimer = DotIntervalTicks;
            var shooter = State.Heroes.FirstOrDefault(h => h.Slot == effect.OwnerSlot);
            if (shooter is null) continue;

            foreach (var victim in State.Heroes)
            {
                if (!victim.Alive || victim.Team == effect.Team) continue;
                long dx = victim.Transform.X - effect.X;
                long dy = victim.Transform.Y - effect.Y;
                if (dx * dx + dy * dy <= (long)effect.Radius * effect.Radius)
                    Damage(shooter, victim, effect.TickDamage);
            }
        }
    }

    private void TickBall()
    {
        if (State.Ball is not { } ball) return;

        if (ball.CelebrateTicks > 0)
        {
            if (--ball.CelebrateTicks > 0) return;
            KickOff();
            return;
        }

        var carrier = State.Heroes.FirstOrDefault(h => h.Slot == ball.CarrierSlot && h.Alive);
        if (carrier is not null)
        {
            ball.X = carrier.Transform.X;
            ball.Y = carrier.Transform.Y;
            return;
        }

        if (ball.CarrierSlot != 0)
        {
            foreach (var hero in State.Heroes) hero.HasBall = false;
            ball.CarrierSlot = 0;
        }

        if (ball.RemainingRange > 0)
        {
            int step = Math.Min(ball.StepPerTick, ball.RemainingRange);
            double radians = ball.DirectionDegrees * Math.PI / 180;
            int nx = State.Map?.ClampX(ball.X + (int)Math.Round(Math.Cos(radians) * step)) ?? ball.X;
            int ny = State.Map?.ClampY(ball.Y + (int)Math.Round(Math.Sin(radians) * step)) ?? ball.Y;
            if (State.BlocksMovement(nx, ny)) ball.RemainingRange = 0;
            else { ball.X = nx; ball.Y = ny; ball.RemainingRange -= step; }
        }

        if (State.Map is not null)
            for (int team = 0; team < 2; team++)
            {
                long dx = ball.X - State.GoalX[team];
                long dy = ball.Y - State.GoalY[team];
                if (dx * dx + dy * dy > (long)GoalRadius * GoalRadius) continue;
                ScoreGoal(team);
                return;
            }

        if (ball.FrozenTicks > 0)
        {
            ball.FrozenTicks--;
            return;
        }

        var taker = State.Heroes.FirstOrDefault(h => h.Alive && Near(h, ball.X, ball.Y, BallPickupRadius));
        if (taker is null) return;

        ball.CarrierSlot = taker.Slot;
        ball.LastTouchSlot = taker.Slot;
        ball.LastTouchTeam = taker.Team;
        ball.RemainingRange = 0;
        taker.HasBall = true;
    }

    private void ScoreGoal(int team)
    {
        if (State.Ball is not { } ball) return;

        State.TeamScores[team & 15] += 1;
        int credit = ball.CarrierSlot != 0 ? ball.CarrierSlot : ball.LastTouchSlot;
        var scorer = State.Heroes.FirstOrDefault(h => h.Slot == credit);
        if (scorer is not null) scorer.Score++;
        foreach (var hero in State.Heroes) hero.HasBall = false;

        Console.WriteLine($"[battle {Id}] гол в ворота команды {1 - team} на тике {State.Ticks}, счёт {State.TeamScores[0]}:{State.TeamScores[1]}");

        ball.CarrierSlot = 0;
        ball.RemainingRange = 0;
        ball.FrozenTicks = 0;
        ball.CelebrateTicks = GoalCelebrationTicks;
        State.GoalSignal = team == 0 ? 1 : -1;
    }

    private void KickOff()
    {
        if (State.Ball is not { } ball || State.Map is not { } pitch) return;

        ball.X = pitch.Width / 2;
        ball.Y = pitch.Height / 2;
        ball.CarrierSlot = 0;
        ball.RemainingRange = 0;
        ball.FrozenTicks = BallLooseTicks;
        State.GoalSignal = 0;

        foreach (var hero in State.Heroes)
        {
            hero.HasBall = false;
            hero.FreezeInputTicks = KickOffFreezeTicks;
            hero.Hitpoints = hero.MaxHitpoints;
            hero.RespawnTicks = 0;
            hero.BurstVolleysLeft = 0;
            hero.CooldownTicks = 0;
            hero.State = 0;
            hero.AttackTicks = 0;
            foreach (var skill in hero.Skills) skill.Ammo = hero.MaxAmmo;

            var (x, y) = SpawnFor(State, hero.Team, hero.IndexInTeam);
            hero.Transform.X = x;
            hero.Transform.Y = y;
            hero.TeamRotation = State.Map?.SpawnRotation(hero.Team) ?? SpawnRotation(hero.Slot);
            hero.EnemyRotation = hero.TeamRotation;
        }

        State.Projectiles.Clear();

        if (State.TeamScores[0] >= GoalsToWin || State.TeamScores[1] >= GoalsToWin) State.GameOver = true;
    }

    private bool KickBall(BattleHero hero)
    {
        if (State.Ball is not { } ball || ball.CarrierSlot != hero.Slot) return false;

        ball.CarrierSlot = 0;
        ball.LastTouchSlot = hero.Slot;
        ball.LastTouchTeam = hero.Team;
        ball.DirectionDegrees = hero.BurstAim;
        ball.StepPerTick = Math.Max(1, BallSpeed / TicksPerSecond);
        ball.RemainingRange = BallKickRange;
        ball.FrozenTicks = BallLooseTicks;
        hero.HasBall = false;
        return true;
    }

    private void TickGems()
    {
        while (State.Items.Count > MaxLooseItems) State.Items.RemoveAt(0);

        var spawner = State.Items.FirstOrDefault(i => i.IsSpawner);
        if (spawner is not null && State.Ticks % GemSpawnTicks == 0
            && State.Items.Count(i => !i.IsSpawner) < MaxLooseGems)
            DropGem(spawner.X, spawner.Y);

        for (int i = State.Items.Count - 1; i >= 0; i--)
        {
            var item = State.Items[i];

            if (item.TriggerRadius > 0)
            {
                var victim = State.Heroes.FirstOrDefault(h => h.Alive && h.Team != item.Team && Near(h, item.X, item.Y, item.TriggerRadius));
                if (victim is null) continue;

                var owner = State.Heroes.FirstOrDefault(h => h.Slot == item.OwnerSlot);
                if (owner is not null)
                    foreach (var caught in State.Heroes)
                    {
                        if (!caught.Alive || caught.Team == item.Team) continue;
                        if (!Near(caught, item.X, item.Y, item.BlastRadius)) continue;
                        Damage(owner, caught, item.Damage);
                    }

                if (item.Blast is { } blast)
                    State.AreaEffects.Add(new BattleAreaEffect
                    {
                        Id = State.NextAreaEffectId++ & 0x3FFF,
                        CsvId = blast.CsvId,
                        OwnerSlot = item.OwnerSlot,
                        Team = item.Team,
                        X = item.X,
                        Y = item.Y,
                        Radius = blast.Radius,
                        Type = blast.Type,
                        TicksLeft = Math.Max(1, blast.TimeMs / TickMilliseconds),
                    });

                Console.WriteLine($"[battle {Id}] взрыв предмета {item.CsvId} на {item.X},{item.Y}");
                State.Items.RemoveAt(i);
                continue;
            }

            if (!item.IsCollectable) continue;

            var taker = State.Heroes.FirstOrDefault(h => h.Alive && Near(h, item.X, item.Y, PickupRadius));
            if (taker is null) continue;

            if (item.IsPowerCube) GiveCube(taker);
            else
            {
                taker.ItemsCarried++;
                taker.Score = taker.ItemsCarried;
            }
            State.Items.RemoveAt(i);
        }
    }

    public void DropGem(int x, int y)
    {
        if (State.GemCsvId < 0) return;
        int angle = Random.Shared.Next(360);
        int distance = Random.Shared.Next(150, 450);
        double radians = angle * Math.PI / 180;
        int gx = x + (int)Math.Round(Math.Cos(radians) * distance);
        int gy = y + (int)Math.Round(Math.Sin(radians) * distance);

        State.Items.Add(new BattleItem
        {
            Id = State.NextItemId++ & 0x3FFF,
            CsvId = State.GemCsvId,
            IsCollectable = true,
            X = State.Map?.ClampX(gx) ?? gx,
            Y = State.Map?.ClampY(gy) ?? gy,
        });
    }

    private static void GiveCube(BattleHero hero)
    {
        hero.PowerCubes++;
        hero.Score = hero.PowerCubes;
        int bonus = Math.Max(1, hero.BaseHitpoints / 10);
        hero.MaxHitpoints += bonus;
        hero.Hitpoints = Math.Min(hero.MaxHitpoints, hero.Hitpoints + bonus);
    }

    private static bool Near(BattleHero hero, int x, int y, int radius)
    {
        long dx = hero.Transform.X - x;
        long dy = hero.Transform.Y - y;
        return dx * dx + dy * dy <= (long)radius * radius;
    }

    private void Shatter(int x, int y)
    {
        if (State.Map is not { } map) return;
        int column = x / BattleMap.TileSize;
        int row = y / BattleMap.TileSize;
        if (!map.IsWeaponBreakable(column, row)) return;
        State.DestroyTile(column, row);
    }

    private static bool Reaches(BattleProjectile p, BattleHero hero)
    {
        long dx = hero.Transform.X - p.X;
        long dy = hero.Transform.Y - p.Y;
        return dx * dx + dy * dy <= (long)p.HitRadius * p.HitRadius;
    }

    private void Simulate()
    {
        bool intro = State.Ticks <= IntroTicks;

        foreach (var hero in State.Heroes)
        {
            if (!hero.Alive)
            {
                if (GameModes.NoRespawn(State.ModeVariation)) continue;
                if (--hero.RespawnTicks <= 0) Respawn(hero);
                continue;
            }

            if (hero.AttackTicks > 0 && --hero.AttackTicks == 0) hero.State = 0;
            if (hero.CooldownTicks > 0) hero.CooldownTicks--;
            if (hero.FreezeInputTicks > 0) hero.FreezeInputTicks--;
            if (hero.UltiActiveTicks > 0) hero.UltiActiveTicks--;
            if (hero.DashTicksLeft > 0) AdvanceDash(hero);
            if (hero.BurstVolleysLeft > 0 && --hero.BurstTimer <= 0) ReleaseVolley(hero);

            var weapon = hero.Skills.First(s => s.Name == "Weapon");
            if (weapon.Ammo < hero.MaxAmmo)
                weapon.Ammo = Math.Min(hero.MaxAmmo, weapon.Ammo + hero.AmmoPerTick);

            if (hero.IsBot && !intro) DriveBot(hero);
        }

        MoveProjectiles();
        TickAreaEffects();
        TickGems();
        TickBall();

        if (intro) return;

        if (GameModes.NoRespawn(State.ModeVariation))
        {
            int sidesAlive = State.Heroes.Where(h => h.Alive).Select(h => h.Team).Distinct().Count();
            if (sidesAlive < 2) State.GameOver = true;
            return;
        }

        if (State.ModeVariation == GameModes.AttackDefend)
        {
            var broken = State.Structures.FirstOrDefault(v => !v.Alive);
            if (broken is not null)
            {
                State.TeamScores[(1 - broken.Team) & 15] = 1;
                State.GameOver = true;
            }
            return;
        }

        if (State.ModeVariation != GameModes.CoinRush) return;

        int lead = State.Heroes.GroupBy(h => h.Team).Select(g => g.Sum(h => h.ItemsCarried)).DefaultIfEmpty(0).Max();
        if (lead < GemsToWin)
        {
            if (_countdown > 0)
                Console.WriteLine($"[battle {Id}] отсчёт сброшен на тике {State.Ticks}: у лидера осталось {lead}");
            _countdown = 0;
            return;
        }

        if (_countdown == 0)
            Console.WriteLine($"[battle {Id}] десять кристаллов набрано на тике {State.Ticks}, пошёл отсчёт: " +
                              string.Join(" / ", State.Heroes.GroupBy(h => h.Team).Select(g => $"команда {g.Key}: {g.Sum(h => h.ItemsCarried)}")));

        _countdown++;
        State.TicksLeft = Math.Max(0, ScoreCountdownTicks - _countdown);
        if (_countdown >= ScoreCountdownTicks) State.GameOver = true;
    }

    private void StepTowardsTarget(BattleHero hero)
    {
        int step = Math.Max(1, hero.SpeedPerSecond / TicksPerSecond);
        int dx = hero.TargetX - hero.Transform.X;
        int dy = hero.TargetY - hero.Transform.Y;
        int distance = (int)Math.Sqrt((long)dx * dx + (long)dy * dy);

        if (distance <= step)
        {
            PlaceAt(hero, hero.TargetX, hero.TargetY);
            return;
        }

        PlaceAt(hero, hero.Transform.X + dx * step / distance, hero.Transform.Y + dy * step / distance);
        hero.TeamRotation = AngleTo(hero.Transform.X, hero.Transform.Y, hero.TargetX, hero.TargetY);
        hero.EnemyRotation = hero.TeamRotation;
    }

    private void StepWithDetour(BattleHero bot)
    {
        if (bot.DetourTicks > 0)
        {
            bot.DetourTicks--;
            int step = Math.Max(1, bot.SpeedPerSecond / TicksPerSecond);
            double radians = bot.DetourAngle * Math.PI / 180;
            int wasX = bot.Transform.X, wasY = bot.Transform.Y;
            PlaceAt(bot, wasX + (int)Math.Round(Math.Cos(radians) * step),
                         wasY + (int)Math.Round(Math.Sin(radians) * step));
            if (wasX == bot.Transform.X && wasY == bot.Transform.Y) bot.DetourTicks = 0;
            return;
        }

        int fromX = bot.Transform.X, fromY = bot.Transform.Y;
        StepTowardsTarget(bot);
        if (fromX != bot.Transform.X || fromY != bot.Transform.Y) return;

        int straight = AngleTo(fromX, fromY, bot.TargetX, bot.TargetY);
        bool clockwise = (State.Ticks + bot.Slot) % 2 == 0;
        bot.DetourAngle = ((clockwise ? straight + 90 : straight - 90) + 360) % 360;
        bot.DetourTicks = DetourTicks;
    }

    private void DriveBot(BattleHero bot)
    {
        var target = State.Heroes
            .Where(h => h.Team != bot.Team && h.Alive)
            .OrderBy(h => DistanceSquared(bot, h))
            .FirstOrDefault();

        if (State.ModeVariation == GameModes.LaserBall && bot.HasBall)
        {
            int goalX = State.GoalX[bot.Team == 0 ? 0 : 1];
            int goalY = State.GoalY[bot.Team == 0 ? 0 : 1];

            if (Span(bot, goalX, goalY) <= (long)BallKickRange * BallKickRange)
            {
                bot.BurstAim = AngleTo(bot.Transform.X, bot.Transform.Y, goalX, goalY);
                KickBall(bot);
                return;
            }

            bot.TargetX = goalX;
            bot.TargetY = goalY;
            bot.HasMoveOrder = true;
            StepWithDetour(bot);
            return;
        }

        if (State.ModeVariation == GameModes.CoinRush)
        {
            var gem = State.Items
                .Where(i => !i.IsSpawner)
                .OrderBy(i => Span(bot, i.X, i.Y))
                .FirstOrDefault();
            if (gem is not null && (target is null || Span(bot, gem.X, gem.Y) < DistanceSquared(bot, target)))
            {
                bot.TargetX = gem.X;
                bot.TargetY = gem.Y;
                bot.HasMoveOrder = true;
                StepWithDetour(bot);
                return;
            }
        }

        if (target is null) return;

        int standoff = Math.Min(BotStandoff, bot.WeaponRange * 3 / 4);
        long distance = DistanceSquared(bot, target);
        if (distance > (long)standoff * standoff)
        {
            bot.TargetX = target.Transform.X;
            bot.TargetY = target.Transform.Y;
            bot.HasMoveOrder = true;
            StepTowardsTarget(bot);
        }
        else
        {
            bot.HasMoveOrder = false;
            bot.TeamRotation = AngleTo(bot.Transform.X, bot.Transform.Y, target.Transform.X, target.Transform.Y);
            bot.EnemyRotation = bot.TeamRotation;
        }

        bool aimed = HasLineOfSight(bot.Transform.X, bot.Transform.Y, target.Transform.X, target.Transform.Y);

        if (bot.UltiCharge >= BattleHero.FullUltiCharge && bot.UltiRow >= 0 && aimed
            && DistanceSquared(bot, target) <= (long)bot.Ulti.Range * bot.Ulti.Range)
        {
            UseSkill(bot, bot.UltiRow, target.Transform.X, target.Transform.Y);
            return;
        }

        if ((State.Ticks + bot.Slot * 7) % BotFirePeriod == 0
            && DistanceSquared(bot, target) <= (long)bot.WeaponRange * bot.WeaponRange
            && aimed)
            FireWeapon(bot, target.Transform.X, target.Transform.Y);
    }

    private void Respawn(BattleHero hero)
    {
        hero.Hitpoints = hero.MaxHitpoints;
        hero.RespawnTicks = 0;
        hero.HasMoveOrder = false;
        hero.State = 0;
        hero.AttackTicks = 0;
        hero.CooldownTicks = 0;
        hero.BurstVolleysLeft = 0;
        hero.DashTicksLeft = 0;
        foreach (var skill in hero.Skills) skill.Ammo = hero.MaxAmmo;

        var (x, y) = SpawnFor(State, hero.Team, hero.IndexInTeam);
        PlaceAt(hero, x, y);
        hero.TeamRotation = State.Map?.SpawnRotation(hero.Team) ?? SpawnRotation(hero.Slot);
        hero.EnemyRotation = hero.TeamRotation;
    }

    public static (int X, int Y) SpawnFor(BattleState state, int team, int indexInTeam)
    {
        if (state.Map is { } map)
        {
            var point = map.SpawnPoint(team, indexInTeam);
            return (point.X, point.Y);
        }
        return SpawnPoint(team, team * 3 + indexInTeam + 1);
    }

    private static readonly int[] SpawnX = [1950, 2550, 3150, 1950, 2550, 3150];
    private static readonly int[] SpawnY = [9750, 9750, 9750, 150, 150, 150];
    private static readonly int[] SpawnAngle = [270, 270, 270, 90, 90, 90];

    public static (int X, int Y) SpawnPoint(int team, int slot)
    {
        int i = Math.Clamp(slot - 1, 0, SpawnX.Length - 1);
        return (SpawnX[i], SpawnY[i]);
    }

    public static int SpawnRotation(int slot) => SpawnAngle[Math.Clamp(slot - 1, 0, SpawnAngle.Length - 1)];

    private static long Span(BattleHero hero, int x, int y)
    {
        long dx = hero.Transform.X - x;
        long dy = hero.Transform.Y - y;
        return dx * dx + dy * dy;
    }

    private static long DistanceSquared(BattleHero a, BattleHero b)
    {
        long dx = a.Transform.X - b.Transform.X;
        long dy = a.Transform.Y - b.Transform.Y;
        return dx * dx + dy * dy;
    }

    private static int AngleTo(int fromX, int fromY, int toX, int toY)
    {
        double degrees = Math.Atan2(toY - fromY, toX - fromX) * 180 / Math.PI;
        if (degrees < 0) degrees += 360;
        return (int)degrees;
    }

    private async Task BroadcastAsync(CancellationToken ct)
    {
        IBattleClient[] snapshot;
        lock (_clientsLock) snapshot = _clients.ToArray();

        foreach (var client in snapshot)
        {
            try
            {
                var payload = BattleStateEncoder.Encode(State, client.LowId);
                await client.SendVisionUpdateAsync(State.Ticks, payload, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[battle {Id}] не удалось отправить состояние игроку {client.LowId}: {ex.Message}");
                RemoveClient(client.LowId);
            }
        }
    }

    private async Task SendOutcomesAsync(CancellationToken ct)
    {
        IBattleClient[] snapshot;
        lock (_clientsLock) snapshot = _clients.ToArray();

        var ranked = State.Heroes
            .OrderByDescending(h => h.Kills)
            .ThenByDescending(h => h.Hitpoints)
            .ToList();

        foreach (var client in snapshot)
        {
            var hero = State.HeroOf(client.LowId);
            if (hero is null) continue;

            int rank = ranked.IndexOf(hero) + 1;
            var roster = State.Heroes
                .OrderByDescending(h => h.Slot == hero.Slot)
                .ThenBy(h => h.Team != hero.Team)
                .ThenBy(h => h.Slot)
                .Select(h => new BattleParticipant(h.Name, h.BrawlerId, h.SkinId, h.Team, h.Kills, h.PowerLevel, h.Slot == hero.Slot))
                .ToList();

            try
            {
                await client.SendBattleOverAsync(
                    new BattleOutcome(client.LowId, hero.BrawlerId, rank, hero.Kills, WonBy(hero), State.GameMode, roster), ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[battle {Id}] не удалось отправить итог игроку {client.LowId}: {ex.Message}");
            }
        }
    }

    private bool WonBy(BattleHero hero)
    {
        int own = State.TeamScores[hero.Team & 15];
        int best = 0;
        foreach (var other in State.Heroes)
        {
            if (other.Team == hero.Team) continue;
            best = Math.Max(best, State.TeamScores[other.Team & 15]);
        }

        if (own != best) return own > best;
        return State.Heroes.Any(h => h.Team == hero.Team && h.Alive)
               && State.Heroes.Where(h => h.Team != hero.Team).All(h => !h.Alive);
    }
}
