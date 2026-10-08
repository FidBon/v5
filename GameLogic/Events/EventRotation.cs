using GameLogic.Csv;

namespace GameLogic.Events;

public sealed class EventRotation(Locations locations, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    private static readonly (string Key, int Hours)[] Slots =
    [
        ("0", 2),
        ("1", 14),
        ("2", 8),
    ];

    private const string WeekendSlot = "3";
    private const string ShowdownSlot = "2";

    public bool Advance(EventState current, EventState next, DateTimeOffset now)
    {
        bool changed = false;
        if (TrioPool.Count == 0) throw new InvalidOperationException("Пул локаций для событий пуст");

        foreach (var (key, hours) in Slots)
        {
            if (current.Slots.ContainsKey(key) && next.Slots.ContainsKey(key)) continue;

            var pool = PoolFor(key);
            var refreshAt = now.AddHours(hours);
            current.Slots[key] = new EventSlot { LocationId = Pick(pool), Timestamp = refreshAt.ToUnixTimeSeconds() };
            next.Slots[key] = new EventSlot { LocationId = Pick(pool), Timestamp = refreshAt.AddHours(hours).ToUnixTimeSeconds() };
            changed = true;
        }

        foreach (var (key, hours) in Slots)
        {
            var slot = current.Slots[key];
            if (now.ToUnixTimeSeconds() < slot.Timestamp) continue;

            var pool = PoolFor(key);
            var refreshAt = now.AddHours(hours);
            int incoming = next.Slots.TryGetValue(key, out var queued) ? queued.LocationId : Pick(pool);

            current.Slots[key] = new EventSlot { LocationId = incoming, Timestamp = refreshAt.ToUnixTimeSeconds() };
            next.Slots[key] = new EventSlot { LocationId = Pick(pool), Timestamp = refreshAt.AddHours(hours).ToUnixTimeSeconds() };
            changed = true;
        }

        changed |= AdvanceWeekendSlot(current, next, now);

        changed |= Sanitize(current);
        changed |= Sanitize(next);

        return changed;
    }

    private bool AdvanceWeekendSlot(EventState current, EventState next, DateTimeOffset now)
    {
        bool weekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var ticketPool = locations.WithGameModes(Locations.TicketModes);
        if (ticketPool.Count == 0) return false;

        if (weekend)
        {
            next.Slots.Remove(WeekendSlot);
            if (current.Slots.ContainsKey(WeekendSlot)) return false;
            current.Slots[WeekendSlot] = new EventSlot
            {
                LocationId = Pick(ticketPool),
                Timestamp = NextWeekdayAt(now, DayOfWeek.Monday, 10).ToUnixTimeSeconds(),
            };
            return true;
        }

        bool changed = current.Slots.Remove(WeekendSlot);
        if (!next.Slots.ContainsKey(WeekendSlot))
        {
            next.Slots[WeekendSlot] = new EventSlot
            {
                LocationId = Pick(ticketPool),
                Timestamp = NextWeekdayAt(now, DayOfWeek.Saturday, 10).ToUnixTimeSeconds(),
            };
            changed = true;
        }
        return changed;
    }

    private bool Sanitize(EventState state)
    {
        bool changed = false;
        foreach (var (key, slot) in state.Slots)
        {
            var allowed = key == WeekendSlot ? locations.WithGameModes(Locations.TicketModes) : PoolFor(key);
            if (allowed.Count == 0 || allowed.Contains(slot.LocationId)) continue;
            slot.LocationId = Pick(allowed);
            changed = true;
        }
        return changed;
    }

    private List<int> TrioPool => locations.ExceptGameModes(Locations.SoloModes);

    private List<int> PoolFor(string key)
    {
        if (key != ShowdownSlot) return TrioPool;
        var showdown = locations.WithGameModes(Locations.ShowdownModes);
        return showdown.Count > 0 ? showdown : TrioPool;
    }

    private int Pick(List<int> pool) => pool[_random.Next(pool.Count)];

    private static DateTimeOffset NextWeekdayAt(DateTimeOffset from, DayOfWeek target, int hour)
    {
        int delta = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (delta == 0) delta = 7;
        var day = from.AddDays(delta);
        return new DateTimeOffset(day.Year, day.Month, day.Day, hour, 0, 0, from.Offset);
    }
}
