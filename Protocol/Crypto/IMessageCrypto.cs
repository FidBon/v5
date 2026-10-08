namespace Protocol.Crypto;

public interface IMessageCrypto
{
    byte[] Decrypt(int messageId, byte[] payload);
    byte[] Encrypt(int messageId, byte[] payload);
}

public sealed class PlainMessageCrypto : IMessageCrypto
{
    public byte[] Decrypt(int messageId, byte[] payload) => payload;
    public byte[] Encrypt(int messageId, byte[] payload) => payload;
}
