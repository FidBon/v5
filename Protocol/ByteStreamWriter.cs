using System.Text;

namespace Protocol;

public sealed class ByteStreamWriter
{
    private readonly List<byte> _buffer = new(256);
    private int _bitOffset;

    public int Length => _buffer.Count;
    public byte[] ToArray() => _buffer.ToArray();

    private void ResetBits() => _bitOffset = 0;

    public void WriteByte(int value)
    {
        ResetBits();
        _buffer.Add((byte)(value & 0xFF));
    }

    public void WriteInt16(int value)
    {
        ResetBits();
        _buffer.Add((byte)((value >> 8) & 0xFF));
        _buffer.Add((byte)(value & 0xFF));
    }

    public void WriteShort(int value) => WriteInt16(value);

    public void WriteInt24(int value)
    {
        ResetBits();
        _buffer.Add((byte)((value >> 16) & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
        _buffer.Add((byte)(value & 0xFF));
    }

    public void WriteInt(int value)
    {
        ResetBits();
        _buffer.Add((byte)((value >> 24) & 0xFF));
        _buffer.Add((byte)((value >> 16) & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
        _buffer.Add((byte)(value & 0xFF));
    }

    public void WriteIntLittleEndian(int value)
    {
        ResetBits();
        _buffer.Add((byte)(value & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
        _buffer.Add((byte)((value >> 16) & 0xFF));
        _buffer.Add((byte)((value >> 24) & 0xFF));
    }

    public void WriteLong(int high, int low)
    {
        WriteInt(high);
        WriteInt(low);
    }

    public void WriteVInt(int data)
    {
        ResetBits();
        if (data < 0)
        {
            if (data >= -63)
            {
                _buffer.Add((byte)((data & 0x3F) | 0x40));
            }
            else if (data >= -8191)
            {
                _buffer.Add((byte)((data & 0x3F) | 0xC0));
                _buffer.Add((byte)((data >> 6) & 0x7F));
            }
            else if (data >= -1048575)
            {
                _buffer.Add((byte)((data & 0x3F) | 0xC0));
                _buffer.Add((byte)(((data >> 6) & 0x7F) | 0x80));
                _buffer.Add((byte)((data >> 13) & 0x7F));
            }
            else if (data >= -134217727)
            {
                _buffer.Add((byte)((data & 0x3F) | 0xC0));
                _buffer.Add((byte)(((data >> 6) & 0x7F) | 0x80));
                _buffer.Add((byte)(((data >> 13) & 0x7F) | 0x80));
                _buffer.Add((byte)((data >> 20) & 0x7F));
            }
            else
            {
                _buffer.Add((byte)((data & 0x3F) | 0xC0));
                _buffer.Add((byte)(((data >> 6) & 0x7F) | 0x80));
                _buffer.Add((byte)(((data >> 13) & 0x7F) | 0x80));
                _buffer.Add((byte)(((data >> 20) & 0x7F) | 0x80));
                _buffer.Add((byte)((data >> 27) & 0x0F));
            }
        }
        else
        {
            if (data <= 63)
            {
                _buffer.Add((byte)(data & 0x3F));
            }
            else if (data <= 8191)
            {
                _buffer.Add((byte)((data & 0x3F) | 0x80));
                _buffer.Add((byte)((data >> 6) & 0x7F));
            }
            else if (data <= 1048575)
            {
                _buffer.Add((byte)((data & 0x3F) | 0x80));
                _buffer.Add((byte)(((data >> 6) & 0x7F) | 0x80));
                _buffer.Add((byte)((data >> 13) & 0x7F));
            }
            else if (data <= 134217727)
            {
                _buffer.Add((byte)((data & 0x3F) | 0x80));
                _buffer.Add((byte)(((data >> 6) & 0x7F) | 0x80));
                _buffer.Add((byte)(((data >> 13) & 0x7F) | 0x80));
                _buffer.Add((byte)((data >> 20) & 0x7F));
            }
            else
            {
                _buffer.Add((byte)((data & 0x3F) | 0x80));
                _buffer.Add((byte)(((data >> 6) & 0x7F) | 0x80));
                _buffer.Add((byte)(((data >> 13) & 0x7F) | 0x80));
                _buffer.Add((byte)(((data >> 20) & 0x7F) | 0x80));
                _buffer.Add((byte)((data >> 27) & 0x0F));
            }
        }
    }

    public void WriteVLong(int high, int low)
    {
        ResetBits();
        WriteVInt(high);
        WriteVInt(low);
    }

    public void WriteString(string? value)
    {
        ResetBits();
        if (value is null)
        {
            WriteInt(-1);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 900000)
        {
            WriteInt(-1);
            return;
        }
        WriteInt(bytes.Length);
        _buffer.AddRange(bytes);
    }

    public void WriteBytes(byte[]? value)
    {
        ResetBits();
        if (value is null)
        {
            WriteInt(-1);
            return;
        }
        WriteInt(value.Length);
        _buffer.AddRange(value);
    }

    public void WriteDataReference(int high = 0, int low = -1)
    {
        WriteVInt(high);
        if (high != 0) WriteVInt(low);
    }

    public void WriteBoolean(bool value)
    {
        if (_bitOffset == 0) _buffer.Add(0);
        if (value) _buffer[^1] |= (byte)(1 << (_bitOffset & 31));
        _bitOffset = (_bitOffset + 1) & 7;
    }

    public void EncodeIntList(IReadOnlyList<int> values)
    {
        WriteVInt(values.Count);
        foreach (var v in values) WriteVInt(v);
    }

    public void WriteLogicLong(int high, int low) => WriteVLong(high, low);
}
