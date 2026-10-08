using System.Text;

namespace Protocol;

public sealed class ByteStreamReader(byte[] data)
{
    private readonly byte[] _data = data;
    private int _position;

    public int Position => _position;
    public int Remaining => _data.Length - _position;

    public byte ReadByte()
    {
        if (_position >= _data.Length) throw new EndOfStreamException("Пакет закончился раньше времени");
        return _data[_position++];
    }

    public byte[] ReadRaw(int count)
    {
        if (count < 0 || _position + count > _data.Length)
            throw new EndOfStreamException($"Запрошено {count} байт, доступно {Remaining}");
        var slice = new byte[count];
        Array.Copy(_data, _position, slice, 0, count);
        _position += count;
        return slice;
    }

    public int ReadInt()
    {
        var b = ReadRaw(4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    public int ReadShort()
    {
        var b = ReadRaw(2);
        return (b[0] << 8) | b[1];
    }

    public bool ReadBoolean() => ReadByte() != 0;

    public int ReadVInt()
    {
        int n = ReadVariableInt(rotate: true);
        return (n >>> 1) ^ -(n & 1);
    }

    private int ReadVariableInt(bool rotate)
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            int b = ReadByte();
            if (rotate && shift == 0)
            {
                int seventh = (b & 0x40) >> 6;
                int msb = (b & 0x80) >> 7;
                int n = b << 1;
                n &= ~0x181;
                b = n | (msb << 7) | seventh;
            }
            result |= (b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0) break;
        }
        return result;
    }

    public (int High, int Low) ReadDataReference()
    {
        int high = ReadVInt();
        return high == 0 ? (0, 0) : (high, ReadVInt());
    }

    public string ReadString()
    {
        int length = ReadInt();
        if (length == -1 || length == 0 || length > 900000) return string.Empty;
        return Encoding.UTF8.GetString(ReadRaw(length));
    }

    public int[] ReadLogicLong() => [ReadVInt(), ReadVInt()];

    public void ReadCommandHeader()
    {
        for (int i = 0; i < 9; i++)
            if (!TryReadVInt(out _)) return;
    }

    public bool TryReadVInt(out int value)
    {
        if (Remaining == 0) { value = 0; return false; }
        try { value = ReadVInt(); return true; }
        catch (EndOfStreamException) { value = 0; return false; }
    }

    public bool TryReadBoolean(out bool value)
    {
        if (Remaining == 0) { value = false; return false; }
        value = ReadBoolean();
        return true;
    }

    public bool TryReadDataReference(out int high, out int low)
    {
        if (Remaining == 0) { high = 0; low = 0; return false; }
        try
        {
            (high, low) = ReadDataReference();
            return true;
        }
        catch (EndOfStreamException)
        {
            high = 0;
            low = 0;
            return false;
        }
    }

    public List<int> DecodeIntList()
    {
        int length = ReadVInt();
        var list = new List<int>(Math.Max(0, length));
        for (int i = 0; i < length; i++) list.Add(ReadVInt());
        return list;
    }
}
