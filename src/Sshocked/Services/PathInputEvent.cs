namespace Sshocked.Services;

public sealed record PathInputEvent(ConsoleKeyInfo? Key = null, int? Column = null, int? Row = null, int Repeat = 1)
{
    public static PathInputEvent? DecodeWindowsRecord(byte[] record)
    {
        if (record.Length != 20) throw new ArgumentException("A console INPUT_RECORD occupies 20 bytes.", nameof(record));
        var type = BitConverter.ToUInt16(record, 0);
        if (type == 1 && BitConverter.ToInt32(record, 4) != 0)
        {
            var state = BitConverter.ToUInt32(record, 16);
            return new(new ConsoleKeyInfo((char)BitConverter.ToUInt16(record, 14),
                (ConsoleKey)BitConverter.ToUInt16(record, 10), (state & 16) != 0, (state & 3) != 0, (state & 12) != 0),
                Repeat: Math.Max(1, (int)BitConverter.ToUInt16(record, 8)));
        }
        if (type == 2 && BitConverter.ToUInt32(record, 16) == 0 && (BitConverter.ToUInt32(record, 8) & 1) != 0)
            return new(Column: BitConverter.ToInt16(record, 4), Row: BitConverter.ToInt16(record, 6));
        return null;
    }
}
