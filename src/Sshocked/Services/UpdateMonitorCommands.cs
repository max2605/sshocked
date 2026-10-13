namespace Sshocked.Services;

public static class UpdateMonitorCommands
{
    public const string Linux = """
        die() { printf '%s\n' "$*" >&2; exit 1; }
        command -v apt-get >/dev/null 2>&1 || exit 127
        simulation=$(LC_ALL=C apt-get --simulate upgrade)
        printf '%s\n' "$simulation" | awk '/^Inst / { n++ } END { printf "UPDATES %d\n", n }'
        [ ! -f /var/run/reboot-required ] && printf 'REBOOT 0\n' || printf 'REBOOT 1\n'
        if [ -d /var/lib/apt/lists ]; then
          index=$(find /var/lib/apt/lists -maxdepth 1 -type f \( -name '*Packages*' -o -name '*InRelease' -o -name '*Release' \) -printf '%T@\n' | sort -n | tail -n 1)
          printf 'INDEX %s\n' "$index"
        else
          printf 'INDEX\n'
        fi
        """;
    public const string Windows = """
        $searcher=(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher();
        $result=$searcher.Search('IsInstalled=0 and IsHidden=0');
        $info=New-Object -ComObject Microsoft.Update.SystemInfo;
        [pscustomobject]@{updates=$result.Updates.Count;reboot=$info.RebootRequired;resultCode=[int]$result.ResultCode}|ConvertTo-Json -Compress
        """;
}
