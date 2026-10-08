using Data.Models;
using Data.Repositories;
using GameLogic;
using Protocol;

namespace Lobby.Handlers;

public sealed class ClubHandler(PlayerRepository players, ClubRepository clubs, Func<IReadOnlyList<LobbySession>> onlineSessions)
{
    private const int ListLimit = 50;

    public async Task HandleJoinableListAsync(LobbySession session, CancellationToken ct)
    {
        var list = clubs.List(ListLimit);

        var w = new ByteStreamWriter();
        w.WriteVInt(list.Count);
        foreach (var club in list)
        {
            w.WriteLong(0, club.ClubId);
            w.WriteString(club.Name);
            w.WriteDataReference(8, club.Badge);
            w.WriteVInt(club.Type);
            w.WriteVInt(club.Members.Count);
            w.WriteVInt(TotalTrophies(club));
            w.WriteVInt(club.RequiredTrophies);
            w.WriteDataReference(0, 1);
            w.WriteVInt(club.Members.Count);
        }

        await session.SendAsync(ServerMessages.JoinableAlliancesList, w, 0, ct);
        Console.WriteLine($"[club] список клубов: {list.Count}");
    }

    public async Task HandleAskForClanAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        var r = new ByteStreamReader(payload);
        r.ReadInt();
        int clubId = r.ReadInt();

        var club = clubs.Find(clubId);
        if (club is null)
        {
            Console.WriteLine($"[club] запрошен несуществующий клуб {clubId}");
            return;
        }
        await SendAllianceDataAsync(session, club, ct);
    }

    private async Task SendAllianceDataAsync(LobbySession session, ClubState club, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteLong(0, club.ClubId);
        w.WriteString(club.Name);
        w.WriteDataReference(8, club.Badge);
        w.WriteVInt(club.Type);
        w.WriteVInt(club.Members.Count);
        w.WriteVInt(TotalTrophies(club));
        w.WriteVInt(club.RequiredTrophies);
        w.WriteDataReference(0, 1);
        w.WriteString(club.Description);

        var members = LoadMembers(club);
        w.WriteVInt(members.Count);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var member in members)
        {
            w.WriteLong(0, member.LowId);
            w.WriteString(member.Name);
            w.WriteVInt(member.ClubRole);
            w.WriteVInt(ExperienceLevel(member.Experience));
            w.WriteVInt(member.Trophies);
            w.WriteVInt(member.PlayerStatus);
            w.WriteVInt((int)Math.Max(0, now - member.LastConnectionTime));
            w.WriteDataReference(28, member.ProfileIcon);
        }

        await session.SendAsync(ServerMessages.AllianceData, w, 0, ct);
    }

    public async Task HandleCreateAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (player.ClubId != 0) return;

        var r = new ByteStreamReader(payload);
        string name = r.ReadString();
        string description = r.ReadString();
        r.TryReadDataReference(out _, out int badge);
        r.TryReadVInt(out int type);
        r.TryReadVInt(out int requiredTrophies);

        if (name.Trim().Length == 0) return;

        var club = new ClubState
        {
            ClubId = clubs.NextClubId(),
            Name = name,
            Description = description,
            Region = player.Region,
            Badge = badge,
            Type = type == 0 ? ClubType.Open : type,
            RequiredTrophies = requiredTrophies,
            Members = [player.Token],
        };

        player.ClubId = club.ClubId;
        player.ClubRole = ClubRole.President;
        players.Save(player);
        clubs.Save(club, player.Trophies);
        clubs.Append(club.ClubId, MembershipEntry(player, ClubStreamEntry.EventJoined));

        await SendAllianceEventAsync(session, 20, ct);
        await SendMyAllianceAsync(session, player, ct);
        await SendClanStreamAsync(session, player, ct);
        Console.WriteLine($"[club] создан клуб {club.ClubId} \"{club.Name}\" игроком {player.LowId}");
    }

    public async Task HandleJoinAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (player.ClubId != 0) return;

        var r = new ByteStreamReader(payload);
        r.ReadInt();
        int clubId = r.ReadInt();

        var club = clubs.Find(clubId);
        if (club is null) return;
        if (club.Type == ClubType.Closed) return;
        if (player.Trophies < club.RequiredTrophies) return;
        if (club.Members.Contains(player.Token)) return;

        club.Members.Add(player.Token);
        player.ClubId = club.ClubId;
        player.ClubRole = ClubRole.Member;
        players.Save(player);
        clubs.Save(club, TotalTrophies(club));
        clubs.Append(club.ClubId, MembershipEntry(player, ClubStreamEntry.EventJoined));

        await SendAllianceEventAsync(session, 40, ct);
        await SendMyAllianceAsync(session, player, ct);
        await SendClanStreamAsync(session, player, ct);
        await BroadcastStreamAsync(club, player.LowId, ct);
        Console.WriteLine($"[club] игрок {player.LowId} вступил в клуб {club.ClubId}");
    }

    public async Task HandleLeaveAsync(LobbySession session, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        var club = clubs.Find(player.ClubId);
        if (club is null) { player.ClubId = 0; players.Save(player); return; }

        club.Members.Remove(player.Token);
        clubs.Append(club.ClubId, MembershipEntry(player, ClubStreamEntry.EventLeft));

        if (club.Members.Count == 0)
        {
            clubs.Delete(club.ClubId);
            Console.WriteLine($"[club] клуб {club.ClubId} распущен: вышел последний участник");
        }
        else
        {
            if (player.ClubRole == ClubRole.President)
            {
                var heir = players.FindByToken(club.Members[0]);
                if (heir is not null)
                {
                    heir.ClubRole = ClubRole.President;
                    players.Save(heir);
                }
            }
            clubs.Save(club, TotalTrophies(club));
            await BroadcastStreamAsync(club, player.LowId, ct);
        }

        int leftClub = player.ClubId;
        player.ClubId = 0;
        player.ClubRole = 0;
        players.Save(player);

        await SendMyAllianceAsync(session, player, ct);
        Console.WriteLine($"[club] игрок {player.LowId} вышел из клуба {leftClub}");
    }

    public async Task HandleChatAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        var club = clubs.Find(player.ClubId);
        if (club is null) return;

        var r = new ByteStreamReader(payload);
        string message = r.ReadString();
        if (message.Trim().Length == 0) return;

        clubs.Append(club.ClubId, new ClubStreamEntry
        {
            EventType = ClubStreamEntry.TypeChat,
            PlayerId = player.LowId,
            PlayerName = player.Name,
            PlayerRole = player.ClubRole,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });

        await BroadcastStreamAsync(club, player.LowId, ct);
        await SendClanStreamAsync(session, player, ct);
    }

    public async Task HandleSearchAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        var r = new ByteStreamReader(payload);
        string query = r.ReadString();

        var found = clubs.List(ListLimit, query.Trim().Length == 0 ? null : query);

        var w = new ByteStreamWriter();
        w.WriteVInt(found.Count);
        foreach (var club in found)
        {
            w.WriteLong(0, club.ClubId);
            w.WriteString(club.Name);
            w.WriteDataReference(8, club.Badge);
            w.WriteVInt(club.Type);
            w.WriteVInt(club.Members.Count);
            w.WriteVInt(TotalTrophies(club));
            w.WriteVInt(club.RequiredTrophies);
            w.WriteDataReference(0, 1);
            w.WriteVInt(club.Members.Count);
        }
        await session.SendAsync(ServerMessages.AllianceSearchResult, w, 0, ct);
        Console.WriteLine($"[club] поиск \"{query}\": найдено {found.Count}");
    }

    public async Task SendMyAllianceAsync(LobbySession session, PlayerState player, CancellationToken ct)
    {
        var club = clubs.Find(player.ClubId);
        var w = new ByteStreamWriter();

        if (club is null)
        {
            w.WriteVInt(0);
            w.WriteBoolean(false);
        }
        else
        {
            w.WriteVInt(OnlineMembers(club));
            w.WriteBoolean(true);
            w.WriteDataReference(25, player.ClubRole);
            w.WriteLong(0, club.ClubId);
            w.WriteString(club.Name);
            w.WriteDataReference(8, club.Badge);
            w.WriteVInt(club.Type);
            w.WriteVInt(club.Members.Count);
            w.WriteVInt(TotalTrophies(club));
            w.WriteDataReference(0, 1);
            w.WriteVInt(club.Members.Count);
        }
        await session.SendAsync(ServerMessages.MyAlliance, w, 0, ct);
    }

    public async Task SendClanStreamAsync(LobbySession session, PlayerState player, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        if (player.ClubId == 0)
        {
            w.WriteVInt(0);
            await session.SendAsync(ServerMessages.ClanStream, w, 0, ct);
            return;
        }

        var chat = clubs.LoadChat(player.ClubId);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        w.WriteVInt(chat.Messages.Count);
        foreach (var entry in chat.Messages)
        {
            w.WriteVInt(entry.EventType);

            w.WriteLogicLong(0, entry.Tick);
            w.WriteLogicLong(0, entry.PlayerId);
            w.WriteString(entry.PlayerName);
            w.WriteVInt(entry.PlayerRole);
            w.WriteVInt((int)Math.Max(0, now - entry.Timestamp));
            w.WriteBoolean(false);

            switch (entry.EventType)
            {
                case ClubStreamEntry.TypeChat:
                    w.WriteString(entry.Message);
                    break;
                case ClubStreamEntry.TypeAllianceEvent:
                    w.WriteVInt(entry.Event);
                    w.WriteBoolean(true);
                    w.WriteLogicLong(0, entry.TargetId);
                    w.WriteString(entry.TargetName);
                    break;
            }
        }
        await session.SendAsync(ServerMessages.ClanStream, w, 0, ct);
    }

    private async Task SendAllianceEventAsync(LobbySession session, int eventId, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteVInt(eventId);
        await session.SendAsync(ServerMessages.AllianceEvent, w, 0, ct);
    }

    private async Task BroadcastStreamAsync(ClubState club, int exceptLowId, CancellationToken ct)
    {
        var memberIds = LoadMembers(club).Select(m => m.LowId).ToHashSet();
        foreach (var other in onlineSessions())
        {
            if (other.Player is not PlayerState p) continue;
            if (p.LowId == exceptLowId || !memberIds.Contains(p.LowId)) continue;
            await SendClanStreamAsync(other, p, ct);
        }
    }

    private List<PlayerState> LoadMembers(ClubState club)
    {
        var result = new List<PlayerState>();
        foreach (var token in club.Members)
        {
            var member = players.FindByToken(token);
            if (member is not null) result.Add(member);
        }
        return result;
    }

    private int TotalTrophies(ClubState club) => LoadMembers(club).Sum(m => m.Trophies);

    private int OnlineMembers(ClubState club)
    {
        var ids = LoadMembers(club).Select(m => m.LowId).ToHashSet();
        return onlineSessions().Count(s => s.Player is PlayerState p && ids.Contains(p.LowId));
    }

    private static ClubStreamEntry MembershipEntry(PlayerState player, int eventId) => new()
    {
        EventType = ClubStreamEntry.TypeAllianceEvent,
        Event = eventId,
        PlayerId = player.LowId,
        PlayerName = player.Name,
        PlayerRole = player.ClubRole,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        TargetId = player.LowId,
        TargetName = player.Name,
    };

    private static int ExperienceLevel(int experience)
    {
        var table = Milestones.ProgressStartExp;
        for (int i = 0; i < table.Length - 1; i++)
            if (table[i] <= experience && experience < table[i + 1])
                return i + 1;
        return table.Length;
    }
}
