using Data.Models;
using Data.Repositories;
using GameLogic;
using GameLogic.Csv;
using GameLogic.Rewards;
using Lobby.Encoders;
using Protocol;

namespace Lobby.Handlers;

public static class ClientCommands
{
    public const int Gatcha = 500;
    public const int BuyCard = 502;
    public const int SetPlayerThumbnail = 505;
    public const int SelectSkin = 506;
    public const int UnlockSkin = 507;
    public const int SelectControlMode = 508;
    public const int BuyCoinsDoubler = 509;
    public const int BuyCoinsBooster = 510;
    public const int BuyBrawler = 513;
    public const int HandleNotification = 514;
    public const int UpgradeBrawler = 516;
}

public sealed class CommandHandler(
    PlayerRepository players,
    GameAssets assets,
    BoxGenerator boxes)
{
    private static readonly int[] UpgradePrices = [20, 50, 120, 300, 1000];
    private static readonly int[] PinUpgrades = [100, 101, 102, 200, 201, 202, 300, 301, 302];
    private static readonly int[] BadgeUpgrades = [110, 111, 210, 211, 310, 311];
    private static readonly int[] MedalUpgrades = [120, 220, 320];
    private static readonly int[] CrestUpgrades = [430, 431, 432];
    private const int StarPowerUpgrade = 540;
    private readonly HashSet<int> _reportedCommands = [];

    public async Task HandleEndClientTurnAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;

        var header = new ByteStreamReader(payload);
        header.TryReadBoolean(out _);
        header.TryReadVInt(out _);
        header.TryReadVInt(out _);
        header.TryReadVInt(out _);
        header.TryReadVInt(out int commandId);

        if (commandId <= 0) return;

        var body = new ByteStreamReader(payload);
        body.ReadCommandHeader();

        bool dirty;
        try
        {
            dirty = await ExecuteAsync(session, player, commandId, body, ct);
        }
        catch (EndOfStreamException)
        {
            Console.WriteLine($"[shop] команда {commandId} пришла обрезанной, пропускаю");
            return;
        }
        if (dirty) players.Save(player);
    }

    private async Task<bool> ExecuteAsync(LobbySession session, PlayerState player, int commandId, ByteStreamReader r, CancellationToken ct)
    {
        switch (commandId)
        {
            case ClientCommands.Gatcha:
            {
                var box = boxes.OpenBox(player, (BoxType)r.ReadVInt());
                if (box.Refused) { Console.WriteLine($"[shop] ящик не открыт: {box.RefusalReason}"); return false; }
                await session.SendAsync(ServerMessages.AvailableServerCommand, RewardEncoder.BuildDeliveryItems(box), 0, ct);
                return true;
            }

            case ClientCommands.BuyBrawler:
            {
                var box = boxes.OpenBrawlerBox(player, r.ReadVInt());
                if (box.Refused) { Console.WriteLine($"[shop] ящик бойца не открыт: {box.RefusalReason}"); return false; }
                await session.SendAsync(ServerMessages.AvailableServerCommand, RewardEncoder.BuildDeliveryItems(box), 0, ct);
                return true;
            }

            case ClientCommands.SelectControlMode:
            {
                int mode = r.ReadVInt();
                if (mode is < 0 or > 2) return false;
                player.ControlMode = mode;
                return true;
            }

            case ClientCommands.BuyCoinsDoubler:
            {
                if (player.Gems < 50) return false;
                player.Gems -= 50;
                player.CoinsDoubler += 1000;
                ClearNotification(player, id: 97, type: 2);
                return true;
            }

            case ClientCommands.BuyCoinsBooster:
            {
                if (player.Gems < 20) return false;
                player.Gems -= 20;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int week = 7 * 24 * 60 * 60;
                player.CoinsBooster = player.CoinsBooster < now
                    ? (int)(now + week)
                    : player.CoinsBooster + week;
                ClearNotification(player, id: 97, type: 1);
                return true;
            }

            case ClientCommands.SetPlayerThumbnail:
            {
                var (_, icon) = r.ReadDataReference();
                if (!assets.Thumbnails.Exists(icon)) return false;
                if (assets.Thumbnails.RequiredTrophies(icon) > player.Trophies) return false;

                var requiredBrawler = assets.Thumbnails.RequiredBrawlerId(icon);
                if (requiredBrawler is not null && !player.Brawlers.ContainsKey(requiredBrawler.Value.ToString()))
                    return false;

                int level = assets.Thumbnails.RequiredExpLevel(icon);
                if (level > 0 && level - 1 < Milestones.ProgressStartExp.Length &&
                    Milestones.ProgressStartExp[level - 1] > player.Experience)
                    return false;

                player.ProfileIcon = icon;
                return true;
            }

            case ClientCommands.SelectSkin:
            {
                var (_, skin) = r.ReadDataReference();
                if (!assets.Skins.Exists(skin)) return false;
                string brawlerKey = assets.Skins.GetBrawlerId(skin).ToString();
                if (!player.Brawlers.TryGetValue(brawlerKey, out var brawler)) return false;
                if (skin != 0 && !brawler.Skins.Contains(skin)) return false;
                brawler.SelectedSkin = skin;
                return true;
            }

            case ClientCommands.UnlockSkin:
            {
                var (_, skin) = r.ReadDataReference();
                if (!assets.Skins.Exists(skin) || assets.Skins.IsDefault(skin)) return false;
                string brawlerKey = assets.Skins.GetBrawlerId(skin).ToString();
                if (!player.Brawlers.TryGetValue(brawlerKey, out var brawler)) return false;
                if (brawler.Skins.Contains(skin)) return false;

                int price = assets.Skins.GetPrice(skin);
                if (player.Gems < price) return false;
                player.Gems -= price;
                brawler.Skins.Add(skin);
                brawler.SelectedSkin = skin;
                return true;
            }

            case ClientCommands.UpgradeBrawler:
            {
                r.ReadVInt();
                int targetBrawler = r.ReadVInt();
                int upgradeType = r.ReadVInt();
                int upgradeTier = r.ReadVInt();
                int upgradeLevel = r.ReadVInt();
                return UpgradeBrawler(player, targetBrawler, upgradeType, upgradeTier, upgradeLevel);
            }

            case ClientCommands.HandleNotification:
            {
                int id = r.ReadVInt();
                MarkNotificationSeen(player, id);
                return true;
            }

            case ClientCommands.BuyCard:
            {
                var (_, card) = r.ReadDataReference();
                Console.WriteLine($"[shop] покупка карты {card} пока не реализована");
                return false;
            }

            case ServerCommands.GiveDeliveryItems:
                return false;

            default:
                if (_reportedCommands.Add(commandId))
                    Console.WriteLine($"[shop] команда {commandId} пока не обработана");
                return false;
        }
    }

    private bool UpgradeBrawler(PlayerState player, int targetBrawler, int type, int tier, int level)
    {
        if (tier < 0 || tier >= UpgradePrices.Length)
        {
            Console.WriteLine($"[shop] прокачка отклонена: ступень {tier} вне диапазона");
            return false;
        }

        string key = targetBrawler.ToString();
        if (!player.Brawlers.TryGetValue(key, out var brawler))
        {
            Console.WriteLine($"[shop] прокачка отклонена: боец {targetBrawler} не открыт");
            return false;
        }

        int baseUpgrade = type * 100 + tier * 10 + level;
        int gain =
            PinUpgrades.Contains(baseUpgrade) ? 1 :
            BadgeUpgrades.Contains(baseUpgrade) ? 2 :
            MedalUpgrades.Contains(baseUpgrade) ? 3 :
            CrestUpgrades.Contains(baseUpgrade) ? 4 :
            baseUpgrade == StarPowerUpgrade ? 8 : 0;
        if (gain == 0)
        {
            Console.WriteLine($"[shop] прокачка отклонена: неизвестное улучшение {baseUpgrade}");
            return false;
        }

        int fullUpgrade = targetBrawler * 1000 + baseUpgrade;
        if (player.PlayerUpgrades.Contains(fullUpgrade))
        {
            Console.WriteLine($"[shop] прокачка отклонена: улучшение {fullUpgrade} уже куплено");
            return false;
        }

        int price = UpgradePrices[tier];
        if (player.UpgradeTokens < price)
        {
            Console.WriteLine($"[shop] прокачка отклонена: нужно {price} токенов, есть {player.UpgradeTokens}");
            return false;
        }

        player.UpgradeTokens -= price;
        player.PlayerUpgrades.Add(fullUpgrade);
        brawler.PowerLevel += gain;
        Console.WriteLine($"[shop] боец {targetBrawler}: улучшение {fullUpgrade} за {price}, уровень силы {brawler.PowerLevel}, осталось токенов {player.UpgradeTokens}");
        return true;
    }

    private static void ClearNotification(PlayerState player, int id, int type)
    {
        var doomed = player.HomeNotifications
            .Where(kv => HomeNotification.From(kv.Value) is { } n && n.Id == id && n.Type == type)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in doomed) player.HomeNotifications.Remove(key);
    }

    private static void MarkNotificationSeen(PlayerState player, int commandId)
    {
        (int Id, int Type)? target = commandId switch
        {
            1 => (99, 0),
            2 => (97, 2),
            3 => (97, 1),
            4 => (98, 0),
            _ => null,
        };
        if (target is null) return;

        foreach (var key in player.HomeNotifications.Keys.ToList())
        {
            var n = HomeNotification.From(player.HomeNotifications[key]);
            if (n is null || n.Id != target.Value.Id) continue;
            if (target.Value.Id == 97 && n.Type != target.Value.Type) continue;
            player.HomeNotifications.Remove(key);
        }
    }
}
