namespace Battle;

public sealed class BitStreamWriter
{
    private readonly List<byte> _buffer = new(256);
    private int _bitIndex;

    public int Length => _buffer.Count;
    public byte[] ToArray() => _buffer.ToArray();

    public void WriteBit(int value)
    {
        if (_bitIndex == 0) _buffer.Add(0xFF);

        int last = _buffer[^1];
        last &= ~(1 << _bitIndex);
        last |= (value & 1) << _bitIndex;
        _buffer[^1] = (byte)last;

        _bitIndex = (_bitIndex + 1) % 8;
    }

    public void WriteSigned(int value, int bitCount)
    {
        WriteBoolean(value >= 0);
        WritePositiveInt(Math.Abs(value), bitCount);
    }

    public void WritePositiveInt(int value, int bitCount)
    {
        Span<byte> bytes =
        [
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 24) & 0xFF),
        ];

        int written = 0;
        int position = 0;
        while (written < bitCount)
        {
            byte current = position < bytes.Length ? bytes[position] : (byte)0;
            for (int p = 0; p < 8 && written < bitCount; p++, written++)
                WriteBit((current >> p) & 1);
            position++;
        }
    }

    public void WriteInt(int value, int bitCount)
    {
        if (value <= -1)
        {
            WritePositiveInt(0, 1);
            WritePositiveInt(-value, bitCount);
        }
        else
        {
            WritePositiveInt(1, 1);
            WritePositiveInt(value, bitCount);
        }
    }

    public void WriteBoolean(bool value) => WritePositiveInt(value ? 1 : 0, 1);

    public void WriteDataReference(int classId, int instanceId)
    {
        WritePositiveInt(classId, 5);
        if (classId >= 1) WritePositiveInt(instanceId, 8);
    }

    public void WriteObjectRunning(int classId, int instanceId)
    {
        WritePositiveInt(classId, 5);
        if (classId >= 1) WritePositiveInt(instanceId, 14);
    }
}
