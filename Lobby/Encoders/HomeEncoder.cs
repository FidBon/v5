using Data.Models;
using GameLogic;
using GameLogic.Csv;
using GameLogic.Events;
using Protocol;

namespace Lobby.Encoders;

public sealed class HomeContext
{
    public required GameAssets Assets { get; init; }
    public required EventState CurrentEvents { get; init; }
    public required EventState NextEvents { get; init; }
    public int MaximumRank { get; init; } = 20;
    public int TicketsPrice { get; init; } = 1;
    public long NextSeasonEndTimestamp { get; init; }
    public int EventCount { get; init; } = 4;

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public string EventText { get; init; } = "67";
}

public static class HomeEncoder
{
    private const int ShopTimestamp = 2017189;

    public static void WriteOwnHomeData(ByteStreamWriter w, PlayerState p, HomeContext ctx)
    {
        WriteClientHome(w, p, ctx);
        WriteClientAvatar(w, p);
        w.WriteVInt(ShopTimestamp);
    }

    public static void WriteClientHome(ByteStreamWriter w, PlayerState p, HomeContext ctx)
    {
        WriteDailyData(w, p, ctx);
        WriteConfData(w, p, ctx);
        w.WriteLong(0, p.LowId);
        WriteNotifications(w, p);
    }

    private static void WriteDailyData(ByteStreamWriter w, PlayerState p, HomeContext ctx)
    {
        long now = ctx.Now.ToUnixTimeSeconds();

        w.WriteVInt(ShopTimestamp);
        w.WriteVInt(10);

        w.WriteVInt(p.Trophies);
        w.WriteVInt(p.HighestTrophies);
        w.WriteVInt(p.TrophyRank);
        w.WriteVInt(p.Experience);

        w.WriteDataReference(28, p.ProfileIcon);

        w.WriteVInt(7);
        for (int i = 0; i < 7; i++) w.WriteVInt(i);

        var selectedSkins = p.Brawlers.Values.Where(b => b.SelectedSkin != 0).Select(b => b.SelectedSkin).ToList();
        w.WriteVInt(selectedSkins.Count);
        foreach (var skin in selectedSkins) w.WriteDataReference(29, skin);

        var ownedSkins = p.Brawlers.Values.SelectMany(b => b.Skins).Where(s => s != 0).ToList();
        w.WriteVInt(ownedSkins.Count);
        foreach (var skin in ownedSkins) w.WriteDataReference(29, skin);

        w.WriteBoolean(false);
        w.WriteVInt(0);
        w.WriteVInt(p.CoinsReward);
        w.WriteVInt(p.TrophiesReward);
        w.WriteBoolean(false);
        w.WriteVInt(p.ControlMode);
        w.WriteBoolean(p.HasBattleHints);
        w.WriteVInt(p.CoinsDoubler);

        int boosterLeft = (int)Math.Max(0, p.CoinsBooster - now);
        w.WriteVInt(boosterLeft);

        w.WriteVInt((int)(ctx.NextSeasonEndTimestamp - now));
        w.WriteBoolean(false);
        w.WriteDataReference(0, 1);
        w.WriteVInt(0);
        w.WriteBoolean(true);
        w.WriteBoolean(true);
        w.EncodeIntList([3, 2, 1, 3, 1]);
        w.EncodeIntList([1, 2, 3, 4, 8]);
        w.EncodeIntList(p.PlayerUpgrades);
        w.EncodeIntList([20, 50, 120, 300, 1000]);
        w.WriteBoolean(false);
    }

    private static void WriteConfData(ByteStreamWriter w, PlayerState p, HomeContext ctx)
    {
        int brawlerTrophiesForReset = ctx.MaximumRank <= 34
            ? Milestones.ProgressStartTrophies[ctx.MaximumRank - 1]
            : Milestones.ProgressStartTrophies[33] + 50 * (ctx.MaximumRank - 34);

        w.WriteVInt(ShopTimestamp);
        w.WriteVInt(100);
        w.WriteVInt(10);
        w.WriteVInt(80);
        w.WriteVInt(10);
        w.WriteVInt(20);
        w.WriteVInt(50);
        w.WriteVInt(50);
        w.WriteVInt(1000);
        w.WriteVInt(7 * 24);
        w.WriteVInt(brawlerTrophiesForReset);
        w.WriteVInt(50);
        w.WriteVInt(9999);
        w.EncodeIntList([1, 2, 5, 10, 20, 60]);
        w.EncodeIntList([3, 10, 20, 60, 200, 500]);
        w.EncodeIntList([0, 30, 80, 170, 350, 0]);

        int[] requiredBrawlers = [0, 3, 5, 7];
        w.WriteVInt(ctx.EventCount);
        for (int i = 0; i < ctx.EventCount; i++)
        {
            w.WriteVInt(i + 1);
            w.WriteVInt(i < requiredBrawlers.Length ? requiredBrawlers[i] : 0);
        }

        WriteEventSlots(w, ctx.CurrentEvents, p, ctx);
        WriteEventSlots(w, ctx.NextEvents, p, ctx);

        w.EncodeIntList([1]);
        w.EncodeIntList([ctx.TicketsPrice]);

        MilestoneWriter.Write(w, ctx.MaximumRank);
    }

    private static void WriteEventSlots(ByteStreamWriter w, EventState state, PlayerState p, HomeContext ctx)
    {
        long now = ctx.Now.ToUnixTimeSeconds();
        var slots = state.Ordered().ToList();

        w.WriteVInt(slots.Count);
        int index = 0;
        foreach (var (_, slot) in slots)
        {
            int secondsLeft = (int)(slot.Timestamp - now);
            w.WriteVInt(index + 1);
            w.WriteVInt(index + 1);
            w.WriteVInt(secondsLeft);
            w.WriteVInt(secondsLeft);
            w.WriteVInt(slot.Tokens);
            w.WriteVInt(8);
            w.WriteVInt(999);
            w.WriteBoolean(false);
            w.WriteBoolean(index == 3);
            w.WriteDataReference(15, slot.LocationId);
            w.WriteVInt(0);
            w.WriteVInt(2);
            w.WriteString(ctx.EventText);
            w.WriteBoolean(false);
            w.WriteVInt(p.Tickets);
            index++;
        }
    }

    private static void WriteNotifications(ByteStreamWriter w, PlayerState p)
    {
        var unseen = p.HomeNotifications
            .Select(kv => HomeNotification.From(kv.Value))
            .Where(n => n is { Seen: false })
            .ToList();

        w.WriteVInt(unseen.Count);
        foreach (var n in unseen)
        {
            w.WriteVInt(n!.Id);
            switch (n.Id)
            {
                case 96:
                    w.WriteVInt(1); w.WriteVInt(2); w.WriteVInt(3);
                    break;
                case 97:
                    w.WriteVInt(n.Type);
                    break;
                case 98:
                    w.WriteVInt(1); w.WriteVInt(2);
                    break;
            }
        }
    }

    public static void WriteClientAvatar(ByteStreamWriter w, PlayerState p)
    {
        int[] resourceIds = [1, 7];
        int[] resourceAmounts = [p.Gold, p.UpgradeTokens];

        for (int i = 0; i < 3; i++) w.WriteLogicLong(0, p.LowId);

        w.WriteString(p.Name);
        w.WriteBoolean(p.Name != "Brawler");
        w.WriteInt(1);

        w.WriteVInt(5);
        w.WriteVInt(p.Brawlers.Count + resourceIds.Length);

        foreach (var brawler in p.Brawlers.Values)
        {
            int cardId = 0;
            foreach (var card in brawler.Cards.Keys)
                if (int.TryParse(card, out var parsed)) cardId = parsed;
            w.WriteDataReference(23, cardId);
            w.WriteVInt(1);
        }

        for (int i = 0; i < resourceIds.Length; i++)
        {
            w.WriteDataReference(5, resourceIds[i]);
            w.WriteVInt(resourceAmounts[i]);
        }

        WriteBrawlerMap(w, p, b => b.Trophies);
        WriteBrawlerMap(w, p, b => b.HighestTrophies);
        w.WriteVInt(0);
        WriteBrawlerMap(w, p, _ => 2);

        w.WriteVInt(p.Gems);
        w.WriteVInt(13);

        for (int i = 0; i < 8; i++) w.WriteVInt(0);

        w.WriteVInt(p.TutorialState);
    }

    private static void WriteBrawlerMap(ByteStreamWriter w, PlayerState p, Func<BrawlerState, int> selector)
    {
        w.WriteVInt(p.Brawlers.Count);
        foreach (var (key, brawler) in p.Brawlers)
        {
            w.WriteDataReference(16, int.TryParse(key, out var id) ? id : 0);
            w.WriteVInt(selector(brawler));
        }
    }
}

public sealed record HomeNotification(int Id, int Type, bool Seen)
{
    public static HomeNotification? From(object? raw)
    {
        if (raw is not System.Text.Json.JsonElement e || e.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;
        int id = e.TryGetProperty("ID", out var idEl) ? idEl.GetInt32() : 0;
        int type = e.TryGetProperty("type", out var typeEl) ? typeEl.GetInt32() : 0;
        bool seen = e.TryGetProperty("seen", out var seenEl) && seenEl.ValueKind == System.Text.Json.JsonValueKind.True;
        return new HomeNotification(id, type, seen);
    }
}
