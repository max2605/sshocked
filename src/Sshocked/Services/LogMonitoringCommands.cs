using System.Globalization;

namespace Sshocked.Services;

internal static class LogMonitoringCommands
{
    public static string Read(string path, LogMonitoringCursor? cursor, bool fromNow, int maximumBytes)
        => "bash -c " + RemoteShell.Quote("set -euo pipefail\n" + Source(path, cursor, fromNow, maximumBytes));

    internal static string Source(string path, LogMonitoringCursor? cursor, bool fromNow, int maximumBytes)
    {
        string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        var settings = $"path={RemoteShell.Quote(path)}\nold_id={RemoteShell.Quote(cursor?.Identity ?? "")}\noffset={Number(cursor?.Offset ?? 0)}\nold_prefix_length={Number(cursor?.PrefixLength ?? 0)}\nold_prefix_hash={RemoteShell.Quote(cursor?.PrefixHash ?? "")}\nold_anchor_offset={Number(cursor?.AnchorOffset ?? 0)}\nold_anchor_length={Number(cursor?.AnchorLength ?? 0)}\nold_anchor_hash={RemoteShell.Quote(cursor?.AnchorHash ?? "")}\nfrom_now={(fromNow ? 1 : 0)}\nmaximum={Number(maximumBytes)}\n";
        return settings + Script;
    }

    private const string Script = """
        die() { printf '%s\n' "$2" >&2; exit "$1"; }
        for tool in stat dd sha256sum base64; do command -v "$tool" >/dev/null 2>&1 || die 127 'LOG_TOOLS_UNAVAILABLE'; done
        [ -e "$path" ] || die 44 'LOG_MISSING'
        [ -r "$path" ] || die 23 'LOG_PERMISSION_DENIED'
        [ -f "$path" ] || die 126 'LOG_NOT_REGULAR'
        exec 9< "$path"
        trap 'exec 9<&-' 0
        fd=/proc/$$/fd/9
        [ -f "$fd" ] || die 126 'LOG_NOT_REGULAR'
        hash() {
          digest=$(dd if="$fd" iflag=skip_bytes,count_bytes skip="$1" count="$2" status=none | sha256sum) || die 74 'LOG_READ_FAILED'
          printf '%s' "${digest%% *}"
        }
        identity=$(stat -Lc '%d:%i' "$fd")
        size=$(stat -Lc '%s' "$fd")
        reset=none
        start=$offset
        if [ -z "$old_id" ]; then
          reset=initial
          [ "$from_now" -eq 0 ] || start=$size
        elif [ "$identity" != "$old_id" ]; then
          reset=identity
          start=0
        elif [ "$size" -lt "$offset" ]; then
          reset=truncate
          start=0
        elif [ "$(hash 0 "$old_prefix_length")" != "$old_prefix_hash" ] || [ "$(hash "$old_anchor_offset" "$old_anchor_length")" != "$old_anchor_hash" ]; then
          reset=anchor
          start=0
        fi
        count=$((size-start))
        [ "$count" -le "$maximum" ] || count=$maximum
        end=$((start+count))
        prefix_length=$size
        [ "$prefix_length" -le 256 ] || prefix_length=256
        prefix_hash=$(hash 0 "$prefix_length")
        anchor_length=$end
        [ "$anchor_length" -le 128 ] || anchor_length=128
        anchor_offset=$((end-anchor_length))
        anchor_hash=$(hash "$anchor_offset" "$anchor_length")
        ends_lf=1
        if [ "$size" -gt 0 ]; then
          last=$(dd if="$fd" iflag=skip_bytes,count_bytes skip=$((size-1)) count=1 status=none | base64 -w0)
          [ "$last" = 'Cg==' ] || ends_lf=0
        fi
        printf 'SSKLOG1 %s %s %s %s %s %s %s %s %s %s %s\n' "$identity" "$size" "$start" "$count" "$prefix_length" "$prefix_hash" "$anchor_offset" "$anchor_length" "$anchor_hash" "$reset" "$ends_lf"
        dd if="$fd" iflag=skip_bytes,count_bytes skip="$start" count="$count" status=none | base64 -w0
        printf '\nSSKLOGEND %s %s %s %s\n' "$(stat -Lc '%d:%i' "$fd")" "$(stat -Lc '%s' "$fd")" "$(hash 0 "$prefix_length")" "$(hash "$anchor_offset" "$anchor_length")"
        """;
}
