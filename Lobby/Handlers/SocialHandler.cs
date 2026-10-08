using Data.Models;
using Data.Repositories;
using GameLogic;
using Lobby.Encoders;
using Protocol;

namespace Lobby.Handlers;

public sealed class SocialHandler(PlayerRepository players)
{
    private const int LeaderboardSize = 100;

    public async Task HandleAskProfileAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        var r = new ByteStreamReader(payload);
        r.ReadInt();
        int lowId = r.ReadInt();

        var target = players.FindByLowId(lowId);
        if (target is null)
        {
            Console.WriteLine($"[profile] запрошен неизвестный игрок {lowId}");
            return;
        }

        var w = new ByteStreamWriter();
        w.WriteLogicLong(0, target.LowId);
        w.WriteString(target.Name);
        w.WriteVInt(0);

        w.WriteVInt(target.Brawlers.Count);
        foreach (var (key, brawler) in target.Brawlers)
        {
            w.WriteDataReference(16, int.TryParse(key, out var id) ? id : 0);
            w.WriteVInt(0);
            w.WriteVInt(brawler.Trophies);
            w.WriteVInt(brawler.HighestTrophies);
            w.WriteVInt(brawler.PowerLevel);
        }

        int[] stats =
        [
            target.TrioWins,
            target.Experience,
            target.Trophies,
            target.HighestTrophies,
            target.Brawlers.Count,
            0,
            28_000_000 + target.ProfileIcon,
            target.SoloWins,
            target.BestTimeBoss,
            target.BestTimeSurvival,
        ];
        w.WriteVInt(stats.Length);
        for (int i = 0; i < stats.Length; i++)
        {
            w.WriteVInt(i + 1);
            w.WriteVInt(stats[i]);
        }

        w.WriteBoolean(false);

        await session.SendAsync(ServerMessages.Profile, w, 0, ct);
        Console.WriteLine($"[profile] отдан профиль id={target.LowId} \"{target.Name}\"");
    }

    public async Task HandleLeaderboardAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState me) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out int isLocal);
        r.TryReadVInt(out int leaderboardType);
        r.TryReadDataReference(out _, out int targetBrawler);

        var top = leaderboardType switch
        {
            0 => players.TopByBrawlerTrophies(targetBrawler, LeaderboardSize),
            1 => players.TopByTrophiesDetailed(LeaderboardSize),
            _ => [],
        };

        var w = new ByteStreamWriter();
        w.WriteVInt(leaderboardType);
        if (leaderboardType == 0) w.WriteDataReference(16, targetBrawler);
        else w.WriteVInt(0);
        w.WriteString(isLocal != 0 ? me.Region : null);

        bool playerEntries = leaderboardType != 2;
        w.WriteVInt(top.Count);
        foreach (var entry in top)
        {
            w.WriteVInt(0);
            w.WriteVInt(entry.LowId);
            w.WriteVInt(1);
            w.WriteVInt(entry.Trophies);
            w.WriteBoolean(playerEntries);
            if (playerEntries)
            {
                w.WriteString(entry.Name);
                w.WriteString("");
                w.WriteVInt(ExperienceLevel(entry.Experience));
                w.WriteDataReference(28, entry.ProfileIcon);
            }
            w.WriteBoolean(!playerEntries);
        }

        int myPlace = top.FindIndex(e => e.LowId == me.LowId) + 1;
        w.WriteVInt(0);
        w.WriteVInt(myPlace > 0 ? myPlace : 1);
        w.WriteVInt(0);
        w.WriteVInt(0);
        w.WriteString(me.Region);

        await session.SendAsync(ServerMessages.Leaderboard, w, 0, ct);
        Console.WriteLine($"[leaderboard] тип {leaderboardType}, записей {top.Count}, моё место {(myPlace > 0 ? myPlace : 1)}");
    }

    public async Task HandleNameCheckAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        var r = new ByteStreamReader(payload);
        string name = r.ReadString();

        var w = new ByteStreamWriter();
        w.WriteVInt(IsNameAcceptable(name) ? 0 : 1);
        w.WriteString(name);
        await session.SendAsync(ServerMessages.AvatarNameCheckResponse, w, 0, ct);
    }

    public async Task HandleChangeNameAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;

        var r = new ByteStreamReader(payload);
        string name = r.ReadString();
        if (!IsNameAcceptable(name))
        {
            Console.WriteLine($"[name] отклонено имя \"{name}\" от id={player.LowId}");
            return;
        }

        player.Name = name;
        players.Save(player);

        var w = new ByteStreamWriter();
        w.WriteVInt(ServerCommands.ChangeAvatarName);
        w.WriteString(name);
        w.WriteVInt(0);
        await session.SendAsync(ServerMessages.AvailableServerCommand, w, 0, ct);
        Console.WriteLine($"[name] id={player.LowId} теперь \"{name}\"");
    }

    private static bool IsNameAcceptable(string name) =>
        name.Length is >= 1 and <= 15 && !name.Contains('\n') && name.Trim().Length > 0;

    private static int ExperienceLevel(int experience)
    {
        var table = Milestones.ProgressStartExp;
        for (int i = 0; i < table.Length - 1; i++)
            if (table[i] <= experience && experience < table[i + 1])
                return i + 1;
        return table.Length;
    }
}
