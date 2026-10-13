using System.Text;

namespace Sshocked.Services;

internal static class CommandSessionOutputReader
{
    public static async Task Read(StreamReader reader, string prefix, bool error, Func<CommandSessionPending?> pending, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var decoder = Encoding.UTF8.GetDecoder();
        var retained = new StringBuilder();
        // StreamReader may wait to fill its character request on an open shell pipe.
        // Read available bytes so short, newline-free output and protocol markers arrive live.
        while (await reader.BaseStream.ReadAsync(buffer.AsMemory(), cancellationToken) is var length && length > 0)
        {
            var count = decoder.GetChars(buffer.AsSpan(0, length), characters, flush: false);
            retained.Append(characters, 0, count);
            Flush(retained, prefix, error, pending);
        }
        var trailing = decoder.GetChars([], characters, flush: true);
        retained.Append(characters, 0, trailing);
        pending()?.Append(retained.ToString(), error);
    }

    private static void Flush(StringBuilder retained, string prefix, bool error, Func<CommandSessionPending?> pending)
    {
        while (retained.Length > 0)
        {
            var value = retained.ToString();
            var start = value.IndexOf(prefix, StringComparison.Ordinal);
            if (start >= 0)
            {
                if (start > 0) { pending()?.Append(value[..start], error); retained.Remove(0, start); value = retained.ToString(); }
                var end = value.IndexOf('\x1f', prefix.Length);
                if (end < 0 && value.Length < prefix.Length + 40) return;
                if (end >= 0)
                {
                    var marker = value[prefix.Length..end];
                    if (pending()?.Complete(marker, error) != true) pending()?.Append(value[..(end + 1)], error);
                    retained.Remove(0, end + 1);
                    continue;
                }
            }
            var keep = Math.Min(value.Length, prefix.Length - 1);
            while (keep > 0 && !value.EndsWith(prefix[..keep], StringComparison.Ordinal)) keep--;
            var emit = value.Length - keep;
            if (emit == value.Length && emit > 0 && char.IsHighSurrogate(value[emit - 1])) emit--;
            if (emit == 0) return;
            pending()?.Append(value[..emit], error);
            retained.Remove(0, emit);
        }
    }
}
