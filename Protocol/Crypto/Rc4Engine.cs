namespace Protocol.Crypto;

public sealed class Rc4Engine
{
    private readonly byte[] _box = new byte[256];
    private int _i;
    private int _j;

    public Rc4Engine(ReadOnlySpan<byte> key)
    {
        if (key.Length == 0) throw new ArgumentException("Пустой ключ RC4", nameof(key));
        for (int i = 0; i < 256; i++) _box[i] = (byte)i;
        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + _box[i] + key[i % key.Length]) & 0xFF;
            (_box[i], _box[j]) = (_box[j], _box[i]);
        }
    }

    public byte[] Update(ReadOnlySpan<byte> input)
    {
        var output = new byte[input.Length];
        for (int k = 0; k < input.Length; k++)
        {
            _i = (_i + 1) & 0xFF;
            _j = (_j + _box[_i]) & 0xFF;
            (_box[_i], _box[_j]) = (_box[_j], _box[_i]);
            output[k] = (byte)(input[k] ^ _box[(_box[_i] + _box[_j]) & 0xFF]);
        }
        return output;
    }

    public void Skip(int count)
    {
        for (int k = 0; k < count; k++)
        {
            _i = (_i + 1) & 0xFF;
            _j = (_j + _box[_i]) & 0xFF;
            (_box[_i], _box[_j]) = (_box[_j], _box[_i]);
        }
    }
}
