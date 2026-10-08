using System.Net;
using System.Net.Sockets;
using Protocol.Crypto;

namespace Lobby;

public sealed class LobbyOptions
{
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8888;
    public string Cryptography { get; init; } = "RC4";
    public string Rc4Key { get; init; } = "fhsd6f86f67rt8fw78fw789we78r9789wer6re";
}

public sealed class LobbyServer(LobbyOptions options, IMessageDispatcher dispatcher)
{
    public Action<LobbySession>? SessionClosed { get; set; }

    private readonly List<LobbySession> _sessions = [];
    private readonly object _sessionsLock = new();

    public int OnlineCount
    {
        get { lock (_sessionsLock) return _sessions.Count; }
    }

    public IReadOnlyList<LobbySession> Snapshot()
    {
        lock (_sessionsLock) return _sessions.ToArray();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Parse(options.Address), options.Port);
        listener.Start();
        Console.WriteLine($"[lobby] слушаю {options.Address}:{options.Port} (крипто: {options.Cryptography})");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleAsync(client, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        IMessageCrypto crypto = options.Cryptography.Equals("RC4", StringComparison.OrdinalIgnoreCase)
            ? new Rc4MessageCrypto(options.Rc4Key)
            : new PlainMessageCrypto();

        var session = new LobbySession(client, crypto, dispatcher);
        lock (_sessionsLock) _sessions.Add(session);
        Console.WriteLine($"[lobby] подключился {session.RemoteAddress} (онлайн: {OnlineCount})");

        try
        {
            await session.RunAsync(ct);
        }
        finally
        {
            lock (_sessionsLock) _sessions.Remove(session);
            try { SessionClosed?.Invoke(session); }
            catch (Exception ex) { Console.WriteLine($"[lobby] обработчик отключения упал: {ex.Message}"); }
            await session.DisposeAsync();
            Console.WriteLine($"[lobby] отключился {session.RemoteAddress} (онлайн: {OnlineCount})");
        }
    }
}
