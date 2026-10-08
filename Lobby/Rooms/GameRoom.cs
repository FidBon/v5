using Data.Models;

namespace Lobby.Rooms;

public sealed class RoomMember
{
    public int LowId { get; init; }
    public string Name { get; set; } = string.Empty;
    public bool Host { get; set; }
    public int BrawlerId { get; set; }
    public int SkinId { get; set; } = 1;
    public int Trophies { get; set; }
    public int HighestTrophies { get; set; }
    public int PowerLevel { get; set; }
    public int Status { get; set; }
    public bool Ready { get; set; }
    public int Team { get; set; }
}

public sealed class GameRoom
{
    public const int TypeRanked = 0;
    public const int TypeFriendly = 1;
    public const int TypeTicketed = 2;

    public int Id { get; init; }
    public int Type { get; set; } = TypeFriendly;
    public bool Practice { get; set; }
    public bool AdvertiseToBand { get; set; }
    public int ClubId { get; set; }
    public int LocationId { get; set; }
    public int EventSlotHigh { get; set; }
    public int EventSlotLow { get; set; }
    public int MaxPlayers { get; set; } = 3;
    public List<RoomMember> Members { get; } = [];
    public List<ClubStreamEntry> Stream { get; } = [];

    private int tick;

    public int NextTick() => ++tick;

    public RoomMember? Member(int lowId) => Members.FirstOrDefault(m => m.LowId == lowId);

    public RoomMember? Owner => Members.FirstOrDefault(m => m.Host);

    public void Append(ClubStreamEntry entry)
    {
        Stream.Add(entry);
        if (Stream.Count > ClubChat.MaxMessages) Stream.RemoveRange(0, Stream.Count - ClubChat.MaxMessages);
    }
}

public sealed class RoomRegistry
{
    private readonly Dictionary<int, GameRoom> rooms = [];
    private readonly Dictionary<int, int> membership = [];
    private readonly object gate = new();

    public GameRoom Create(int hostLowId)
    {
        lock (gate)
        {
            int id;
            do id = Random.Shared.Next(100000, 1000000); while (rooms.ContainsKey(id));
            var room = new GameRoom { Id = id };
            rooms[id] = room;
            membership[hostLowId] = id;
            return room;
        }
    }

    public GameRoom? Find(int roomId)
    {
        lock (gate) return rooms.GetValueOrDefault(roomId);
    }

    public GameRoom? RoomOf(int lowId)
    {
        lock (gate) return membership.TryGetValue(lowId, out int id) ? rooms.GetValueOrDefault(id) : null;
    }

    public void Bind(int lowId, int roomId)
    {
        lock (gate) membership[lowId] = roomId;
    }

    public void Unbind(int lowId)
    {
        lock (gate) membership.Remove(lowId);
    }

    public void Drop(GameRoom room)
    {
        lock (gate)
        {
            rooms.Remove(room.Id);
            foreach (var member in room.Members) membership.Remove(member.LowId);
        }
    }

    public List<GameRoom> Advertised(int clubId)
    {
        lock (gate) return rooms.Values
            .Where(r => r.AdvertiseToBand && r.ClubId == clubId && r.Members.Count > 0)
            .Take(3)
            .ToList();
    }
}
