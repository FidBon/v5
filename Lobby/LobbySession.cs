using System.Net;
using System.Net.Sockets;
using Protocol;
using Protocol.Crypto;

namespace Lobby;

public sealed class LobbySession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly IMessageCrypto _crypto;
    private readonly IMessageDispatcher _dispatcher;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public IPEndPoint? RemoteEndPoint { get; }
    public object? Player { get; set; }
    public int LowId { get; set; }

    public bool IsConnected { get; private set; } = true;

    public LobbySession(TcpClient client, IMessageCrypto crypto, IMessageDispatcher dispatcher)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        _crypto = crypto;
        _dispatcher = dispatcher;
        RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
    }

    public string RemoteAddress => RemoteEndPoint?.Address.ToString() ?? "?";

    public async Task RunAsync(CancellationToken ct)
    {
        var header = new byte[PacketFrame.HeaderSize];
        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(header, ct)) return;

            var (messageId, length, version) = PacketFrame.ParseHeader(header);
            if (length < 0 || length > 4 * 1024 * 1024)
            {
                Console.WriteLine($"[lobby] {RemoteAddress}: некорректная длина {length} у пакета {messageId}");
                return;
            }

            var payload = new byte[length];
            if (length > 0 && !await ReadExactAsync(payload, ct)) return;

            byte[] decrypted;
            try
            {
                decrypted = _crypto.Decrypt(messageId, payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[lobby] {RemoteAddress}: не расшифровался пакет {messageId}: {ex.Message}");
                return;
            }

            try
            {
                await _dispatcher.DispatchAsync(this, messageId, version, decrypted, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[lobby] {RemoteAddress}: обработчик {messageId} упал: {ex}");
            }
        }
    }

    public async Task SendAsync(int messageId, byte[] payload, int version = 0, CancellationToken ct = default)
    {
        if (!IsConnected) return;

        var encrypted = _crypto.Encrypt(messageId, payload);
        var header = PacketFrame.BuildHeader(messageId, encrypted.Length, version);
        var frame = new byte[header.Length + encrypted.Length];
        header.CopyTo(frame, 0);
        encrypted.CopyTo(frame, header.Length);

        try
        {
            await _writeLock.WaitAsync(ct);
        }
        catch (ObjectDisposedException)
        {
            IsConnected = false;
            return;
        }

        try
        {
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            IsConnected = false;
        }
        finally
        {
            try { _writeLock.Release(); }
            catch (ObjectDisposedException) { IsConnected = false; }
        }
    }

    public Task SendAsync(int messageId, ByteStreamWriter writer, int version = 0, CancellationToken ct = default)
        => SendAsync(messageId, writer.ToArray(), version, ct);

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n;
            try
            {
                n = await _stream.ReadAsync(buffer.AsMemory(read), ct);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                return false;
            }
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _writeLock.Dispose();
        _stream.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

public interface IMessageDispatcher
{
    Task DispatchAsync(LobbySession session, int messageId, int version, byte[] payload, CancellationToken ct);
}
