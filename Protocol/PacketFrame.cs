namespace Protocol;

public readonly record struct PacketFrame(int MessageId, int Version, byte[] Payload)
{
    public const int HeaderSize = 7;

    public static byte[] BuildHeader(int messageId, int payloadLength, int version)
    {
        return
        [
            (byte)((messageId >> 8) & 0xFF),
            (byte)(messageId & 0xFF),
            (byte)((payloadLength >> 16) & 0xFF),
            (byte)((payloadLength >> 8) & 0xFF),
            (byte)(payloadLength & 0xFF),
            (byte)((version >> 8) & 0xFF),
            (byte)(version & 0xFF),
        ];
    }

    public static (int MessageId, int Length, int Version) ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize) throw new ArgumentException("Заголовок короче 7 байт", nameof(header));
        int id = (header[0] << 8) | header[1];
        int length = (header[2] << 16) | (header[3] << 8) | header[4];
        int version = (header[5] << 8) | header[6];
        return (id, length, version);
    }
}
