using Battle;
using Data.Models;
using GameLogic;
using GameLogic.Csv;
using Lobby.Encoders;
using Lobby.Rooms;
using Protocol;

namespace Lobby.Handlers;

public sealed class RoomHandler(
    GameAssets assets,
    Matchmaker matchmaker,
    RoomRegistry registry,
    Func<IReadOnlyList<LobbySession>> onlineSessions,
    Func<LobbySession, int, IBattleClient> battleClient,
    Func<int, int> locationForEventSlot,
    int fallbackLocationId,
    int playersPerMatch)
{
    private const int ErrorRoomFull = 2;
    private const int ErrorNotFound = 6;
    private const int ErrorAlreadyInRoom = 10;

    private const int EventCreated = 101;
    private const int EventJoined = 102;
    private const int EventLeft = 103;
    private const int EventKicked = 104;

    public async Task HandleCreateAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out int slotHigh);
        r.TryReadVInt(out int slotLow);
        r.TryReadVInt(out int type);

        if (registry.RoomOf(player.LowId) is not null)
        {
            await SendErrorAsync(session, ErrorAlreadyInRoom, ct);
            return;
        }

        var room = registry.Create(player.LowId);
        if (slotLow > 0)
        {
            room.Type = type;
            room.EventSlotHigh = slotHigh;
            room.EventSlotLow = slotLow;
            room.LocationId = ResolveLocation(slotLow);
        }
        else
        {
            room.Type = GameRoom.TypeFriendly;
            room.LocationId = fallbackLocationId;
        }

        room.ClubId = player.ClubId;
        room.MaxPlayers = MaxPlayersFor(room.LocationId, room.Type);
        room.Members.Add(NewMember(player, host: true, team: 0));
        Append(room, EventCreated, player, player.LowId, player.Name);

        Console.WriteLine($"[рум] {player.LowId} создал комнату {room.Id}: тип {room.Type}, локация {room.LocationId} " +
                          $"\"{assets.Locations.GetName(room.LocationId)}\", мест {room.MaxPlayers}");

        await SendRoomAsync(session, room, ct);
        await SendStreamAsync(session, room, null, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    public async Task HandleJoinAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out _);
        r.TryReadVInt(out int roomId);

        if (registry.RoomOf(player.LowId) is not null)
        {
            await SendErrorAsync(session, ErrorAlreadyInRoom, ct);
            return;
        }

        var room = registry.Find(roomId);
        if (room is null)
        {
            Console.WriteLine($"[рум] {player.LowId} ищет комнату {roomId}, такой нет");
            await SendErrorAsync(session, ErrorNotFound, ct);
            return;
        }

        if (room.Members.Count >= room.MaxPlayers)
        {
            await SendErrorAsync(session, ErrorRoomFull, ct);
            return;
        }

        room.Members.Add(NewMember(player, host: false, team: BalancedTeam(room)));
        registry.Bind(player.LowId, room.Id);
        int tick = Append(room, EventJoined, player, player.LowId, player.Name);

        Console.WriteLine($"[рум] {player.LowId} вошёл в комнату {room.Id}, игроков {room.Members.Count}/{room.MaxPlayers}");

        await SendRoomAsync(session, room, ct);
        await SendStreamAsync(session, room, null, ct);
        await BroadcastAsync(room, player.LowId, tick, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    public async Task HandleSpectateAsync(LobbySession session, byte[] payload, CancellationToken ct) =>
        await HandleJoinAsync(session, payload, ct);

    public async Task HandleKickAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { Host: true }) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out _);
        r.TryReadVInt(out int targetId);

        if (room.Member(targetId) is not { } target || target.LowId == player.LowId) return;

        room.Members.Remove(target);
        registry.Unbind(target.LowId);
        int tick = Append(room, EventKicked, player, target.LowId, target.Name);

        Console.WriteLine($"[рум] {player.LowId} выгнал {target.LowId} из комнаты {room.Id}");

        if (SessionOf(target.LowId) is { } kicked) await SendLeftAsync(kicked, 1, ct);
        await BroadcastAsync(room, -1, tick, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    public async Task HandleLeaveAsync(LobbySession session, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;

        int tick = Append(room, EventLeft, player, player.LowId, player.Name);
        await SendLeftAsync(session, 0, ct);
        await LeaveAsync(room, player.LowId, tick, ct);
    }

    public async Task HandleChangeMemberSettingsAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { } member) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out _);
        r.TryReadDataReference(out _, out int brawlerId);

        if (!player.Brawlers.TryGetValue(brawlerId.ToString(), out var brawler)) return;

        member.BrawlerId = brawlerId;
        member.SkinId = brawler.SelectedSkin;
        member.Trophies = brawler.Trophies;
        member.HighestTrophies = brawler.HighestTrophies;
        member.PowerLevel = brawler.PowerLevel;

        await BroadcastAsync(room, -1, null, ct);
    }

    public async Task HandleSetReadyAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { } member) return;

        var r = new ByteStreamReader(payload);
        member.Ready = r.ReadBoolean();

        await BroadcastAsync(room, -1, null, ct);

        if (room.Members.Count == 0 || room.Members.Any(m => !m.Ready)) return;
        await StartAsync(room, ct);
    }

    public async Task HandleTogglePracticeAsync(LobbySession session, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { Host: true }) return;

        room.Practice = !room.Practice;
        await BroadcastAsync(room, -1, null, ct);
    }

    public async Task HandleToggleSideAsync(LobbySession session, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        await BroadcastAsync(room, -1, null, ct);
    }

    public async Task HandleChatAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;

        var r = new ByteStreamReader(payload);
        string text = r.ReadString();
        if (text.Length == 0) return;

        int tick = room.NextTick();
        room.Append(new ClubStreamEntry
        {
            EventType = ClubStreamEntry.TypeChat,
            Tick = tick,
            PlayerId = player.LowId,
            PlayerName = player.Name,
            PlayerRole = player.ClubRole,
            Message = text,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });

        foreach (var target in SessionsOf(room))
            await SendStreamAsync(target, room, tick, ct);
    }

    public async Task HandlePostAdAsync(LobbySession session, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (player.ClubId == 0) return;

        room.AdvertiseToBand = !room.AdvertiseToBand;
        room.ClubId = player.ClubId;

        await BroadcastAsync(room, -1, null, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    public async Task HandleMemberStatusAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { } member) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out int status);
        member.Status = status;

        await BroadcastAsync(room, -1, null, ct);
    }

    public async Task HandleSetRankedLocationAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { Host: true }) return;

        var r = new ByteStreamReader(payload);
        r.TryReadVInt(out int slotHigh);
        r.TryReadVInt(out int slotLow);

        room.Type = GameRoom.TypeRanked;
        room.EventSlotHigh = slotHigh;
        room.EventSlotLow = slotLow;
        room.LocationId = ResolveLocation(slotLow);
        room.MaxPlayers = MaxPlayersFor(room.LocationId, room.Type);

        await BroadcastAsync(room, -1, null, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    public async Task HandleSetLocationAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;
        if (room.Member(player.LowId) is not { Host: true }) return;

        var r = new ByteStreamReader(payload);
        r.TryReadDataReference(out _, out int locationId);
        if (!assets.Locations.IsBattleReady(locationId)) return;

        room.Type = GameRoom.TypeFriendly;
        room.EventSlotHigh = 0;
        room.EventSlotLow = 0;
        room.LocationId = locationId;
        room.MaxPlayers = MaxPlayersFor(room.LocationId, room.Type);

        await BroadcastAsync(room, -1, null, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    public void HandleDisconnect(LobbySession session)
    {
        if (session.Player is not PlayerState player) return;
        if (registry.RoomOf(player.LowId) is not { } room) return;

        int tick = Append(room, EventLeft, player, player.LowId, player.Name);
        _ = LeaveAsync(room, player.LowId, tick, CancellationToken.None);
    }

    public async Task SendAllianceTeamsAsync(LobbySession session, PlayerState player, CancellationToken ct)
    {
        if (player.ClubId == 0) return;
        var teams = registry.Advertised(player.ClubId);

        var w = new ByteStreamWriter();
        RoomEncoder.WriteAllianceTeams(w, teams, room =>
            assets.Locations.GetGameMode(room.LocationId) is "Survival" or "BossFight"
                ? GameRoom.TypeTicketed
                : room.Type);
        await session.SendAsync(ServerMessages.AllianceTeams, w, 0, ct);
    }

    private async Task LeaveAsync(GameRoom room, int lowId, int tick, CancellationToken ct)
    {
        if (room.Member(lowId) is not { } member) return;

        bool wasHost = member.Host;
        room.Members.Remove(member);
        registry.Unbind(lowId);

        if (room.Members.Count == 0)
        {
            registry.Drop(room);
            Console.WriteLine($"[рум] комната {room.Id} распущена");
            await BroadcastAllianceTeamsAsync(room, ct);
            return;
        }

        if (wasHost) room.Members[0].Host = true;

        Console.WriteLine($"[рум] {lowId} вышел из комнаты {room.Id}, осталось {room.Members.Count}");

        await BroadcastAsync(room, -1, tick, ct);
        await BroadcastAllianceTeamsAsync(room, ct);
    }

    private async Task StartAsync(GameRoom room, CancellationToken ct)
    {
        var requests = new List<MatchRequest>();
        foreach (var member in room.Members)
        {
            member.Ready = false;
            member.Status = 5;
            if (SessionOf(member.LowId) is not { } session || session.Player is not PlayerState player) continue;
            requests.Add(new MatchRequest(
                player.LowId, player.Name, member.BrawlerId, member.SkinId, member.PowerLevel,
                room.LocationId, battleClient(session, player.LowId)));
        }

        if (requests.Count == 0) return;

        foreach (var session in SessionsOf(room))
        {
            await SendGameStartingAsync(session, room, ct);
            if (session.Player is PlayerState p)
                await BattleHandler.SendMatchmakingStatusAsync(session, p, room.MaxPlayers, ct);
        }

        Console.WriteLine($"[рум] комната {room.Id} стартует: игроков {requests.Count}, локация {room.LocationId} " +
                          $"\"{assets.Locations.GetName(room.LocationId)}\"");

        matchmaker.StartParty(requests);
    }

    private int ResolveLocation(int eventSlot)
    {
        int requested = locationForEventSlot(eventSlot);
        return requested >= 0 && assets.Locations.IsBattleReady(requested) ? requested : fallbackLocationId;
    }

    private int MaxPlayersFor(int locationId, int type)
    {
        string mode = assets.Locations.GetGameMode(locationId);
        if (mode is "BattleRoyale" or "BattleRoyaleTeam")
        {
            if (type == GameRoom.TypeFriendly) return 10;
            return mode == "BattleRoyale" ? 1 : 2;
        }
        if (mode is "Survival" or "BossFight") return 3;
        if (type == GameRoom.TypeFriendly) return playersPerMatch;
        return Math.Max(1, playersPerMatch / 2);
    }

    private static int BalancedTeam(GameRoom room) =>
        room.Members.Count(m => m.Team == 0) <= room.Members.Count(m => m.Team == 1) ? 0 : 1;

    private RoomMember NewMember(PlayerState player, bool host, int team)
    {
        var member = new RoomMember
        {
            LowId = player.LowId,
            Name = player.Name,
            Host = host,
            Team = team,
            Status = 0,
        };

        var chosen = player.Brawlers.FirstOrDefault();
        if (chosen.Key is not null && int.TryParse(chosen.Key, out int brawlerId))
        {
            member.BrawlerId = brawlerId;
            member.SkinId = chosen.Value.SelectedSkin;
            member.Trophies = chosen.Value.Trophies;
            member.HighestTrophies = chosen.Value.HighestTrophies;
            member.PowerLevel = chosen.Value.PowerLevel;
        }
        return member;
    }

    private int Append(GameRoom room, int eventId, PlayerState actor, int targetId, string targetName)
    {
        int tick = room.NextTick();
        room.Append(new ClubStreamEntry
        {
            EventType = ClubStreamEntry.TypeAllianceEvent,
            Event = eventId,
            Tick = tick,
            PlayerId = actor.LowId,
            PlayerName = actor.Name,
            PlayerRole = actor.ClubRole,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TargetId = targetId,
            TargetName = targetName,
        });
        return tick;
    }

    private LobbySession? SessionOf(int lowId) =>
        onlineSessions().FirstOrDefault(s => s.Player is PlayerState p && p.LowId == lowId);

    private List<LobbySession> SessionsOf(GameRoom room)
    {
        var ids = room.Members.Select(m => m.LowId).ToHashSet();
        return onlineSessions().Where(s => s.Player is PlayerState p && ids.Contains(p.LowId)).ToList();
    }

    private async Task BroadcastAsync(GameRoom room, int exceptLowId, int? tick, CancellationToken ct)
    {
        foreach (var session in SessionsOf(room))
        {
            if (session.Player is PlayerState p && p.LowId == exceptLowId) continue;
            if (tick is { } value) await SendStreamAsync(session, room, value, ct);
            await SendRoomAsync(session, room, ct);
        }
    }

    private async Task BroadcastAllianceTeamsAsync(GameRoom room, CancellationToken ct)
    {
        if (room.ClubId == 0) return;
        foreach (var session in onlineSessions())
        {
            if (session.Player is not PlayerState p || p.ClubId != room.ClubId) continue;
            await SendAllianceTeamsAsync(session, p, ct);
        }
    }

    private static async Task SendRoomAsync(LobbySession session, GameRoom room, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        RoomEncoder.WriteRoom(w, room);
        await session.SendAsync(ServerMessages.TeamState, w, 0, ct);
    }

    private static async Task SendStreamAsync(LobbySession session, GameRoom room, int? tick, CancellationToken ct)
    {
        var entries = tick is { } value ? room.Stream.Where(e => e.Tick == value).ToList() : room.Stream;
        var w = new ByteStreamWriter();
        RoomEncoder.WriteStream(w, room, entries, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await session.SendAsync(ServerMessages.TeamStream, w, 0, ct);
    }

    private static async Task SendErrorAsync(LobbySession session, int error, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        RoomEncoder.WriteError(w, error);
        await session.SendAsync(ServerMessages.TeamError, w, 0, ct);
    }

    private static async Task SendLeftAsync(LobbySession session, int reason, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteInt(reason);
        await session.SendAsync(ServerMessages.TeamLeft, w, 0, ct);
    }

    private static async Task SendGameStartingAsync(LobbySession session, GameRoom room, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        RoomEncoder.WriteGameStarting(w, room.LocationId);
        await session.SendAsync(ServerMessages.TeamGameStarting, w, 0, ct);
    }
}
