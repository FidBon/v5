using System.Security.Cryptography;
using Data.Models;
using Data.Repositories;
using Lobby.Encoders;
using Protocol;

namespace Lobby.Handlers;

public sealed class LoginOptions
{
    public int StartingTutorialState { get; init; } = 2;
    public string ExpectedFingerprint { get; init; } = string.Empty;
    public string FingerprintJson { get; init; } = string.Empty;
    public string AssetsUrl { get; init; } = string.Empty;
}

public sealed class LoginHandler(
    PlayerRepository players,
    LoginOptions options,
    Func<PlayerState, HomeContext> homeContextFactory)
{
    public static LoginPayload Decode(byte[] payload)
    {
        var r = new ByteStreamReader(payload);
        int highId = r.ReadInt();
        int lowId = r.ReadInt();
        string token = r.ReadString();
        int major = r.ReadInt();
        int minor = r.ReadInt();
        int build = r.ReadInt();
        string fingerprint = r.ReadString();
        _ = r.ReadString();
        string deviceId = r.ReadString();
        _ = r.ReadString();
        string device = r.ReadString();
        _ = r.ReadVInt();
        string region = "EN";
        try
        {
            var locale = r.ReadString();
            var parts = locale.Split('-');
            if (parts.Length > 1 && parts[1].Length > 0) region = parts[1];
        }
        catch (EndOfStreamException)
        {
        }

        return new LoginPayload
        {
            HighId = highId,
            LowId = lowId,
            Token = token,
            MajorVersion = major,
            MinorVersion = minor,
            Build = build,
            FingerprintSha = fingerprint,
            DeviceId = deviceId,
            Device = device,
            Region = region,
        };
    }

    public async Task HandleAsync(LobbySession session, byte[] payload, CancellationToken ct)
    {
        var login = Decode(payload);

        if (options.ExpectedFingerprint.Length > 0 &&
            !string.Equals(login.FingerprintSha, options.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[login] {session.RemoteAddress}: отпечаток {Short(login.FingerprintSha)} вместо {Short(options.ExpectedFingerprint)}, отправляю патч");
            await SendLoginFailedAsync(session, 7, "Обновление ассетов", ct, options.FingerprintJson);
            return;
        }

        string token = login.Token.Length > 0 ? login.Token : GenerateToken();
        var player = players.FindByToken(token);

        if (player is null)
        {
            player = PlayerState.CreateNew(token, players.NextLowId(), options.StartingTutorialState);
            player.Region = login.Region;
            players.Insert(player);
            Console.WriteLine($"[login] новый аккаунт id={player.LowId} с {session.RemoteAddress}");
        }

        if (player.Banned)
        {
            await SendLoginFailedAsync(session, 11, "Аккаунт заблокирован", ct);
            return;
        }

        player.Region = login.Region;
        player.PlayerStatus = 1;
        player.LastConnectionTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        player.CoinsReward = 0;
        player.TrophiesReward = 0;
        players.Save(player);

        session.Player = player;
        session.LowId = player.LowId;

        await SendLoginOkAsync(session, player, login, token, ct);
        await SendFriendListAsync(session, player, ct);
        await SendOwnHomeDataAsync(session, player, ct);
        if (OnLoggedIn is not null) await OnLoggedIn(session, player, ct);

        Console.WriteLine($"[login] вошёл id={player.LowId} \"{player.Name}\" кубков={player.Trophies} бойцов={player.Brawlers.Count}");
    }

    private async Task SendLoginOkAsync(LobbySession session, PlayerState player, LoginPayload login, string token, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteLong(0, player.LowId);
        w.WriteLong(0, player.LowId);
        w.WriteString(token);
        w.WriteString("467606826913688");
        w.WriteString("G:325378671");
        w.WriteInt(login.MajorVersion);
        w.WriteInt(login.MinorVersion);
        w.WriteInt(login.Build);
        w.WriteString("-dev");
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteString(null);
        w.WriteString(null);
        w.WriteString(null);
        w.WriteInt(0);
        w.WriteString(null);
        w.WriteString(player.Region);
        w.WriteString(null);
        w.WriteInt(1);
        w.WriteString(null);
        w.WriteString(null);
        w.WriteString(null);
        await session.SendAsync(ServerMessages.LoginOk, w, version: 1, ct);
    }

    public Func<LobbySession, PlayerState, CancellationToken, Task>? OnLoggedIn { get; set; }

    private static async Task SendFriendListAsync(LobbySession session, PlayerState player, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        w.WriteInt(0);
        w.WriteInt(0);
        await session.SendAsync(ServerMessages.FriendList, w, 0, ct);
    }

    public async Task SendOwnHomeDataAsync(LobbySession session, PlayerState player, CancellationToken ct)
    {
        var w = new ByteStreamWriter();
        HomeEncoder.WriteOwnHomeData(w, player, homeContextFactory(player));
        await session.SendAsync(ServerMessages.OwnHomeData, w, 0, ct);
    }

    private async Task SendLoginFailedAsync(LobbySession session, int errorCode, string message, CancellationToken ct, string fingerprint = "")
    {
        var w = new ByteStreamWriter();
        w.WriteInt(errorCode);
        w.WriteString(fingerprint);
        w.WriteString(null);
        w.WriteString(options.AssetsUrl);
        w.WriteString("https://github.com/Super-brawl-team/Obiad-Brawl");
        w.WriteString(message);
        w.WriteInt(0);
        w.WriteBoolean(false);
        w.WriteString(null);
        w.WriteString(null);
        w.WriteInt(0);
        await session.SendAsync(ServerMessages.LoginFailed, w, 0, ct);
    }

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;

    private static string GenerateToken()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[40];
        for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }
}
