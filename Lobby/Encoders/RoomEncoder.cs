using Data.Models;
using Lobby.Rooms;
using Protocol;

namespace Lobby.Encoders;

public static class RoomEncoder
{
    public static void WriteRoom(ByteStreamWriter w, GameRoom room)
    {
        w.WriteVInt(room.Type);
        w.WriteBoolean(room.Practice);
        w.WriteVInt(room.MaxPlayers);
        w.WriteLong(0, room.Id);
        w.WriteVInt(1);
        w.WriteBoolean(room.AdvertiseToBand);
        w.WriteVLong(room.EventSlotHigh, room.EventSlotLow);
        w.WriteDataReference(15, room.LocationId);
        w.WriteVInt(room.Members.Count);
        foreach (var member in room.Members)
        {
            w.WriteBoolean(member.Host);
            w.WriteLong(0, member.LowId);
            w.WriteString(member.Name);
            w.WriteVInt(69);
            w.WriteDataReference(16, member.BrawlerId);
            w.WriteDataReference(0, 1);
            w.WriteVInt(member.Trophies);
            w.WriteVInt(member.HighestTrophies);
            w.WriteVInt(member.PowerLevel);
            w.WriteVInt(member.Status);
            w.WriteBoolean(member.Ready);
            w.WriteVInt(member.Team);
        }
    }

    public static void WriteStream(ByteStreamWriter w, GameRoom room, IReadOnlyList<ClubStreamEntry> entries, long now)
    {
        w.WriteLogicLong(0, room.Id);
        w.WriteVInt(entries.Count);
        foreach (var entry in entries)
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
    }

    public static void WriteAllianceTeams(ByteStreamWriter w, IReadOnlyList<GameRoom> rooms, Func<GameRoom, int> typeOf)
    {
        w.WriteBoolean(true);
        w.WriteVInt(rooms.Count);
        foreach (var room in rooms)
        {
            w.WriteVInt(typeOf(room));
            w.WriteBoolean(false);
            w.WriteVInt(room.Members.Count);
            w.WriteVInt(room.MaxPlayers);
            w.WriteLong(0, room.Id);
            w.WriteVInt(Math.Max(0, room.EventSlotHigh));
            w.WriteVInt(Math.Max(0, room.EventSlotLow));
            w.WriteDataReference(15, room.LocationId);
            w.WriteVInt(room.AdvertiseToBand ? 2 : 1);
            w.WriteString(room.Owner?.Name ?? string.Empty);
        }
    }

    public static void WriteError(ByteStreamWriter w, int error)
    {
        w.WriteVInt(error);
        w.WriteVInt(0);
    }

    public static void WriteGameStarting(ByteStreamWriter w, int locationId)
    {
        w.WriteVInt(0);
        w.WriteVInt(0);
        w.WriteDataReference(15, locationId);
    }
}
