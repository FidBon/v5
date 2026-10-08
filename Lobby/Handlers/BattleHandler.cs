using Battle;
using Data.Models;
using Data.Repositories;
using GameLogic;
using GameLogic.Battle;
using GameLogic.Csv;
using Protocol;

namespace Lobby.Handlers;

public sealed class SessionBattleClient(LobbySession session, int lowId, BattleHandler handler) : IBattleClient
{
    public int LowId { get; } = lowId;
    public LobbySession Session { get; } = session;

    public Task SendVisionUpdateAsync(int ticks, byte[] state, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteVInt(ticks);
        w.WriteVInt(0);
        w.WriteVInt(1);
        w.WriteInt(state.Length);
        var payload = w.ToArray();
        var full = new byte[payload.Length + state.Length];
        payload.CopyTo(full, 0);
        state.CopyTo(full, payload.Length);
        return Session.SendAsync(ServerMessages.VisionUpdate, full, 0, ct);
    }

    public Task SendBattleOverAsync(BattleOutcome outcome, CancellationToken ct) =>
        handler.SendBattleEndAsync(Session, outcome, ct);
}

public sealed class BattleHandler(
    PlayerRepository players,
    GameAssets assets,
    Matchmaker matchmaker,
    int maximumRank,
    int fallbackLocationId,
    Func<int, int> locationForEventSlot)
{
    public async Task HandleMatchmakeRequestAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out _);
        r.TryReadDataReference(out _, out int brawlerId);
        r.TryReadVInt(out int eventSlot);

        if (!player.Brawlers.ContainsKey(brawlerId.ToString()))
        {
            Console.WriteLine($"[battle] игрок {player.LowId} просит боя за чужого бойца {brawlerId}");
            return;
        }

        int locationId = ResolveLocation(eventSlot);
        var brawler = player.Brawlers[brawlerId.ToString()];

        var client = new SessionBattleClient(session, player.LowId, this);
        matchmaker.Enqueue(new MatchRequest(player.LowId, player.Name, brawlerId, brawler.SelectedSkin, brawler.PowerLevel, locationId, client));

        Console.WriteLine($"[battle] игрок {player.LowId} в очереди: слот {eventSlot} -> локация {locationId} " +
                          $"\"{assets.Locations.GetName(locationId)}\" ({assets.Locations.GetGameMode(locationId)})");
        int seats = GameModes.PlayersPerMatch(GameModes.Variation(assets.Locations.GetGameMode(locationId)), 6);
        await SendMatchmakingStatusAsync(session, player, seats, ct);
    }

    private int ResolveLocation(int eventSlot)
    {
        int requested = locationForEventSlot(eventSlot);
        if (requested >= 0 && assets.Locations.IsBattleReady(requested)) return requested;

        Console.WriteLine($"[battle] слот {eventSlot} не дал играбельной локации — играем на " +
                          $"{fallbackLocationId} \"{assets.Locations.GetName(fallbackLocationId)}\"");
        return fallbackLocationId;
    }

    public static async Task SendMatchmakingStatusAsync(LobbySession session, PlayerState player, int seats, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteInt(20);
        w.WriteInt(1);
        w.WriteString(player.Name);
        w.WriteBoolean(true);
        w.WriteLong(0, player.LowId);
        w.WriteInt(seats);
        await session.SendAsync(ServerMessages.MatchmakingStatus, w, 0, ct);
    }

    public static async Task SendStartLoadingAsync(LobbySession session, BattleState state, int viewerLowId, CancellationToken ct)
    {
        int ownTeam = state.HeroOf(viewerLowId)?.Team ?? 0;

        var w = new ByteStreamWriter();
        w.WriteInt(state.Heroes.Count);
        w.WriteInt(0);
        w.WriteInt(0);

        w.WriteInt(state.Heroes.Count);
        foreach (var hero in state.Heroes)
        {
            w.WriteLong(0, hero.IsBot ? 100 + hero.Slot : hero.OwnerLowId);
            w.WriteString(hero.Name);
            w.WriteVInt(hero.Slot - 1);
            w.WriteVInt(hero.Team == ownTeam ? 0 : 1);
            w.WriteVInt(0);
            w.WriteInt(0);
            w.WriteDataReference(16, hero.BrawlerId);
            w.WriteDataReference(hero.SkinId == 0 ? 0 : 29, hero.SkinId);
            w.WriteBoolean(false);
        }

        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteVInt(1);
        w.WriteVInt(1);
        w.WriteVInt(2);
        w.WriteBoolean(false);
        w.WriteDataReference(15, state.LocationId);

        await session.SendAsync(ServerMessages.StartLoading, w, 0, ct);
    }

    public async Task CancelMatchmakingAsync(LobbySession session, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        matchmaker.Cancel(player.LowId);
        await session.SendAsync(ServerMessages.MatchmakingCancelled, new ByteStreamWriter(), 0, ct);
        Console.WriteLine($"[battle] игрок {player.LowId} вышел из подбора");
    }

    public void HandleDisconnect(LobbySession session)
    {
        if (session.Player is not PlayerState player) return;
        matchmaker.Cancel(player.LowId);
        matchmaker.LeaveRoom(player.LowId);
    }

    public void HandleClientInput(LobbySession session, byte[] payload)
    {
        if (session.Player is not PlayerState player) return;
        var room = matchmaker.RoomOf(player.LowId);
        if (room is null) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out _);
        r.TryReadVInt(out _);
        r.TryReadVInt(out int count);
        for (int i = 0; i < count && r.Remaining > 0; i++)
        {
            r.TryReadVInt(out _);
            r.TryReadVInt(out _);
            if (!r.TryReadVInt(out int type)) break;
            if (!r.TryReadVInt(out int x)) break;
            if (!r.TryReadVInt(out int y)) break;
            room.EnqueueInput(new BattleInput(player.LowId, type, x, y));
        }
    }

    public async Task SendBattleEndAsync(LobbySession session, BattleOutcome outcome, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;

        string brawlerKey = outcome.BrawlerId.ToString();
        if (!player.Brawlers.TryGetValue(brawlerKey, out var brawler)) return;

        bool isTrio = assets.Locations.IsTrioMode(outcome.GameMode);
        bool isTicket = assets.Locations.IsTicketMode(outcome.GameMode);
        int outcomeOrRank = isTrio ? (outcome.Victory ? 0 : 1) : outcome.Rank;

        var rewards = player.TutorialState < 2
            ? new BattleRewards(0, 0, 0, 0)
            : TrophyRules.Calculate(isTrio, outcomeOrRank, brawler.Trophies, isTicket);

        int doubler = Math.Min(rewards.Coins, player.CoinsDoubler);
        player.CoinsDoubler -= doubler;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int booster = player.CoinsBooster > now ? rewards.Coins : 0;

        player.Trophies = Math.Max(0, player.Trophies + rewards.Trophies);
        player.TrophiesReward = rewards.Trophies;
        brawler.Trophies = Math.Max(0, brawler.Trophies + rewards.Trophies);
        if (brawler.Trophies > brawler.HighestTrophies) brawler.HighestTrophies = brawler.Trophies;
        if (player.Trophies > player.HighestTrophies) player.HighestTrophies = player.Trophies;

        player.Experience += rewards.Experience + rewards.StarExperience;
        player.Gold += rewards.Coins + doubler + booster;
        player.CoinsReward = rewards.Coins + doubler + booster;
        if (isTrio && outcome.Victory) player.TrioWins++;
        else if (!isTrio && outcome.Rank == 1) player.SoloWins++;
        player.BattleId = 0;
        players.Save(player);

        var w = new ByteStreamWriter();
        w.WriteVInt(GameModeType(outcome.GameMode));
        w.WriteVInt(0);
        w.WriteVInt(rewards.Coins);
        w.WriteVInt(6969);
        w.WriteVInt(0);
        w.WriteBoolean(false);
        w.WriteVInt(isTrio ? (outcome.Victory ? 0 : 1) : outcome.Rank);
        w.WriteVInt(rewards.Trophies);
        w.WriteDataReference(28, player.ProfileIcon);
        w.WriteBoolean(false);
        w.WriteBoolean(true);
        w.WriteBoolean(player.TutorialState < 2);
        w.WriteVInt(50);
        w.WriteVInt(booster);
        w.WriteVInt(doubler);
        w.WriteVInt(0);

        var roster = outcome.Participants;
        int ownTeam = roster.Count == 0 ? 0 : roster[0].Team;
        var star = roster.OrderByDescending(p => p.Kills).ThenBy(p => p.IsPlayer ? 0 : 1).FirstOrDefault();

        w.WriteVInt(roster.Count);
        foreach (var member in roster)
        {
            w.WriteString(member.Name);
            w.WriteBoolean(member.IsPlayer);
            w.WriteBoolean(member.Team != ownTeam);
            w.WriteBoolean(ReferenceEquals(member, star));
            w.WriteDataReference(16, member.BrawlerId);
            w.WriteDataReference(member.SkinId == 0 ? 0 : 29, member.SkinId);
            w.WriteVInt((member.IsPlayer ? brawler.Trophies : 0) + 6974);
            w.WriteVInt(member.IsPlayer ? brawler.PowerLevel : member.PowerLevel);
        }

        w.WriteVInt(2);
        w.WriteVInt(0);
        w.WriteVInt(rewards.Experience);
        w.WriteVInt(8);
        w.WriteVInt(rewards.StarExperience);

        int brawlerTrophiesBefore = brawler.Trophies - rewards.Trophies;
        int experienceBefore = player.Experience - rewards.Experience - rewards.StarExperience;
        int trophiesBefore = player.Trophies - rewards.Trophies;

        bool trophyMilestone = CrossesThreshold(TrophyThresholds(maximumRank), brawlerTrophiesBefore, brawler.Trophies)
                               && brawler.Trophies < brawler.HighestTrophies;
        bool expMilestone = CrossesThreshold(Milestones.ProgressStartExp, experienceBefore, player.Experience);

        bool rankMilestone = false;
        int rankRewardIndex = 0;
        for (int x = 0; x < Milestones.ProgressStart.Length - 2; x++)
        {
            if (Milestones.ProgressStart[x] > trophiesBefore || trophiesBefore >= Milestones.ProgressStart[x + 1]) continue;
            if (Milestones.ProgressStart[x + 1] <= player.Trophies && player.Trophies < Milestones.ProgressStart[x + 2]
                && x == player.TrophyRank + 1)
            {
                rankMilestone = true;
                rankRewardIndex = x + 1;
                player.TrophyRank++;
            }
            break;
        }

        if (trophyMilestone) player.Gold += 10;
        if (expMilestone) player.Gold += 20;
        if (rankMilestone && rankRewardIndex < Milestones.PrimaryLevelUpReward.Length)
            player.Gold += Milestones.PrimaryLevelUpReward[rankRewardIndex];

        w.WriteVInt((trophyMilestone ? 1 : 0) + (expMilestone ? 1 : 0) + (rankMilestone ? 1 : 0));

        if (expMilestone)
        {
            w.WriteVInt(5);
            for (int i = 0; i < 4; i++) w.WriteVInt(0);
            w.WriteVInt(1);
            w.WriteVInt(12);
            w.WriteVInt(20);
            w.WriteDataReference(5, 1);
            w.WriteVInt(0);
        }
        if (trophyMilestone)
        {
            w.WriteVInt(1);
            for (int i = 0; i < 4; i++) w.WriteVInt(0);
            w.WriteVInt(1);
            w.WriteVInt(1);
            w.WriteVInt(10);
            w.WriteDataReference(5, 1);
            w.WriteVInt(0);
        }
        if (rankMilestone)
        {
            int primary = rankRewardIndex < Milestones.PrimaryLevelUpReward.Length ? Milestones.PrimaryLevelUpReward[rankRewardIndex] : 0;
            int secondary = rankRewardIndex < Milestones.SecondaryLevelUpReward.Length ? Milestones.SecondaryLevelUpReward[rankRewardIndex] : 0;
            w.WriteVInt(6);
            for (int i = 0; i < 4; i++) w.WriteVInt(0);
            w.WriteVInt(1);
            w.WriteVInt(13);
            w.WriteVInt(primary);
            w.WriteDataReference(5, 1);
            w.WriteVInt(1);
            w.WriteVInt(13);
            w.WriteVInt(secondary);
            w.WriteDataReference(5, 1);
            player.Gold += secondary;
        }

        w.WriteVInt(2);
        w.WriteVInt(1);
        w.WriteVInt(brawler.Trophies);
        w.WriteVInt(brawler.HighestTrophies);
        w.WriteVInt(5);
        w.WriteVInt(player.Experience);
        w.WriteVInt(player.Experience);

        w.WriteBoolean(true);
        MilestoneWriter.Write(w, maximumRank);

        players.Save(player);
        await session.SendAsync(ServerMessages.BattleEnd, w, 0, ct);

        Console.WriteLine($"[battle] итог id={player.LowId}: кубки {rewards.Trophies:+#;-#;0}, монеты {rewards.Coins}, место {outcome.Rank}");
    }

    private static int[] TrophyThresholds(int maximumRank)
    {
        var list = new List<int>(Milestones.ProgressStartTrophies);
        for (int i = list.Count; i < Math.Max(maximumRank, list.Count); i++)
            list.Add(Milestones.ProgressStartTrophies[33] + 50 * (i - 33));
        return [.. list];
    }

    private static bool CrossesThreshold(int[] thresholds, int before, int after)
    {
        for (int x = 0; x < thresholds.Length - 2; x++)
        {
            if (thresholds[x] > before || before >= thresholds[x + 1]) continue;
            return thresholds[x + 1] <= after && after < thresholds[x + 2];
        }
        return false;
    }

    private static int GameModeType(string gameMode) => gameMode switch
    {
        "BattleRoyale" or "BattleRoyaleTeam" => 2,
        "Survival" => 3,
        "BossFight" => 4,
        _ => 1,
    };
}
