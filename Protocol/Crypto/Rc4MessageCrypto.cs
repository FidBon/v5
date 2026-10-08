using System.Text;

namespace Protocol.Crypto;

public sealed class Rc4MessageCrypto : IMessageCrypto
{
    private const string Nonce = "nonce";

    private readonly Rc4Engine _incoming;
    private readonly Rc4Engine _outgoing;

    public Rc4MessageCrypto(string rc4Key)
    {
        var seed = Encoding.UTF8.GetBytes(rc4Key + Nonce);
        _incoming = new Rc4Engine(seed);
        _outgoing = new Rc4Engine(seed);
        _incoming.Update(seed);
        _outgoing.Update(seed);
    }

    public byte[] Decrypt(int messageId, byte[] payload) => _incoming.Update(payload);
    public byte[] Encrypt(int messageId, byte[] payload) => _outgoing.Update(payload);
}
