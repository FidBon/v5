using Patcher;
using Battle;
using Data;
using Data.Repositories;
using GameLogic.Csv;
using GameLogic.Events;
using GameLogic.Rewards;
using Lobby;
using Lobby.Encoders;
using Lobby.Handlers;
using Lobby.Rooms;
using Server;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Obiad Brawl — сервер на .NET, поддержка клиента v5");

var settings = ServerSettings.Load("Settings.json");

var database = new ObiadDatabase(settings.DatabasePath);
database.Migrate();
var players = new PlayerRepository(database);
var events = new EventRepository(database);
Console.WriteLine($"[db] {settings.DatabasePath}: аккаунтов {players.Count()}");

var assets = new GameAssets(settings.AssetsRoot);
Console.WriteLine($"[assets] {settings.AssetsRoot}: бойцов {assets.Characters.GetPlayableBrawlers().Count}, локаций {assets.Locations.Table.Count}");

var patcherOptions = new PatcherOptions
{
    Address = settings.PatcherAddress,
    Port = settings.PatcherPort,
    Url = settings.PatcherUrl,
    Content = settings.PatcherContent,
    BaseFingerprint = settings.PatcherFingerprint,
};
var patch = settings.PatcherEnabled ? new PatchContent(patcherOptions) : null;

var rotation = new EventRotation(assets.Locations);

var currentEvents = events.Load<EventState>(1);
var nextEvents = events.Load<EventState>(2);
if (rotation.Advance(currentEvents, nextEvents, DateTimeOffset.UtcNow))
{
    events.Save(1, currentEvents);
    events.Save(2, nextEvents);
}
Console.WriteLine($"[events] слотов сейчас: {currentEvents.Slots.Count}, в очереди: {nextEvents.Slots.Count}");
Console.WriteLine($"[assets] карт в maps.csv: {assets.Maps.Count}");
Console.WriteLine($"[battle] запасная карта: {settings.BattleLocationId} \"{assets.Locations.GetName(settings.BattleLocationId)}\", режим {assets.Locations.GetGameMode(settings.BattleLocationId)}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var matchmaker = new Matchmaker(assets, new MatchmakerOptions
{
    PlayersPerMatch = settings.PlayersPerMatch,
    FillWithBotsAfter = TimeSpan.FromSeconds(settings.MatchmakingFillWithBotsSeconds),
    MaxTicks = settings.BattleMaxTicks,
    ShowdownBoxes = settings.ShowdownBoxes,
});

var loginHandler = new LoginHandler(
    players,
    new LoginOptions
    {
        StartingTutorialState = settings.StartingTutorialState,
        ExpectedFingerprint = patch is { HasPatch: true } ? patch.Sha : string.Empty,
        FingerprintJson = patch?.Json ?? string.Empty,
        AssetsUrl = patcherOptions.PublicUrl(),
    },
    _ => new HomeContext
    {
        Assets = assets,
        CurrentEvents = currentEvents,
        NextEvents = nextEvents,
        MaximumRank = settings.MaximumRank,
        TicketsPrice = settings.TicketsPrice,
        NextSeasonEndTimestamp = settings.NextSeasonEndTimestamp,
        EventText = settings.EventText,
    });

int LocationForEventSlot(int eventSlot)
{
    var slots = currentEvents.Ordered().ToList();
    int index = eventSlot - 1;
    return index >= 0 && index < slots.Count ? slots[index].Value.LocationId : -1;
}

var battleHandler = new BattleHandler(
    players, assets, matchmaker, settings.MaximumRank, settings.BattleLocationId, LocationForEventSlot);

LobbyServer? lobbyRef = null;
var clubHandler = new ClubHandler(players, new ClubRepository(database), () => lobbyRef?.Snapshot() ?? []);
var roomHandler = new RoomHandler(
    assets,
    matchmaker,
    new RoomRegistry(),
    () => lobbyRef?.Snapshot() ?? [],
    (session, lowId) => new SessionBattleClient(session, lowId, battleHandler),
    LocationForEventSlot,
    settings.BattleLocationId,
    settings.PlayersPerMatch);

loginHandler.OnLoggedIn = async (session, player, ct) =>
{
    await clubHandler.SendMyAllianceAsync(session, player, ct);
    await clubHandler.SendClanStreamAsync(session, player, ct);
    await roomHandler.SendAllianceTeamsAsync(session, player, ct);
};

var lobby = new LobbyServer(
    new LobbyOptions
    {
        Address = settings.Address,
        Port = settings.Port,
        Cryptography = settings.Cryptography,
        Rc4Key = settings.Rc4Key,
    },
    new LobbyDispatcher(
        loginHandler,
        new CommandHandler(players, assets, new BoxGenerator(assets)),
        battleHandler,
        new SocialHandler(players),
        clubHandler,
        roomHandler));
lobbyRef = lobby;

lobby.SessionClosed = session =>
{
    battleHandler.HandleDisconnect(session);
    roomHandler.HandleDisconnect(session);
};

var tasks = new List<Task> { lobby.RunAsync(cts.Token) };

tasks.Add(Loop("events", TimeSpan.FromMinutes(1), cts.Token, () =>
{
    if (!rotation.Advance(currentEvents, nextEvents, DateTimeOffset.UtcNow)) return Task.CompletedTask;
    events.Save(1, currentEvents);
    events.Save(2, nextEvents);
    Console.WriteLine("[events] слоты обновлены");
    return Task.CompletedTask;
}));

tasks.Add(Loop("matchmaker", TimeSpan.FromSeconds(1), cts.Token, async () =>
{
    foreach (var (room, participants) in matchmaker.FormMatches(DateTimeOffset.UtcNow))
    {
        foreach (var participant in participants)
        {
            if (participant.Client is SessionBattleClient bound)
                await BattleHandler.SendStartLoadingAsync(bound.Session, room.State, participant.LowId, cts.Token);
        }

        _ = Task.Run(async () =>
        {
            try { await room.RunAsync(cts.Token); }
            catch (Exception ex) { Console.WriteLine($"[battle {room.Id}] упал: {ex.Message}"); }
            finally { matchmaker.Release(room); }
        }, cts.Token);
    }
}));

if (patch is not null) tasks.Add(new PatcherHttpServer(patcherOptions, patch).RunAsync(cts.Token));

try
{
    await Task.WhenAll(tasks);
}
catch (OperationCanceledException)
{
}
Console.WriteLine("[server] остановлен");
return 0;

static async Task Loop(string name, TimeSpan period, CancellationToken ct, Func<Task> body)
{
    using var timer = new PeriodicTimer(period);
    try
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await body(); }
            catch (Exception ex) { Console.WriteLine($"[{name}] ошибка: {ex.Message}"); }
        }
    }
    catch (OperationCanceledException)
    {
    }
}
