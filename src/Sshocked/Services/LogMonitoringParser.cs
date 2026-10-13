using System.Globalization;

namespace Sshocked.Services;

internal static class LogMonitoringParser
{
    public static LogMonitoringRead Parse(string output, int maximumBytes)
    {
        var lines = output.TrimEnd('\r', '\n').Split('\n');
        if (lines.Length != 3) throw new InvalidDataException("Log probe returned invalid framing.");
        var head = lines[0].TrimEnd('\r').Split(' ');
        var tail = lines[2].TrimEnd('\r').Split(' ');
        if (head.Length != 12 || head[0] != "SSKLOG1" || tail.Length != 5 || tail[0] != "SSKLOGEND")
            throw new InvalidDataException("Log probe returned invalid metadata.");
        long Number(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException("Log probe returned an invalid offset.");
        var size = Number(head[2]);
        var start = Number(head[3]);
        var count = Number(head[4]);
        var prefixLength = Number(head[5]);
        var anchorOffset = Number(head[7]);
        var anchorLength = Number(head[8]);
        if (start > size || count > maximumBytes || count > size - start || prefixLength > 256 || anchorLength > 128
            || anchorOffset > size || anchorLength > size - anchorOffset || !Hash(head[6]) || !Hash(head[9]) || !Identity(head[1])
            || head[10] is not ("initial" or "identity" or "truncate" or "anchor" or "none") || head[11] is not ("0" or "1"))
            throw new InvalidDataException("Log probe metadata exceeds its limits.");
        if (tail[1] != head[1] || Number(tail[2]) < size || tail[3] != head[6] || tail[4] != head[9])
            throw new InvalidDataException("The opened log generation changed during its bounded read. Coverage is uncertain.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(lines[1]); }
        catch (FormatException) { throw new InvalidDataException("Log probe payload is not valid base64."); }
        if (bytes.LongLength != count) throw new InvalidDataException("Log probe payload length does not match its metadata.");
        return new(head[1], size, start, (int)prefixLength, head[6], anchorOffset, (int)anchorLength, head[9], head[10], head[11] == "1", bytes);
    }

    internal static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool Identity(string value) => value.Split(':') is [var device, var inode]
        && long.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out _) && long.TryParse(inode, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
