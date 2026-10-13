namespace Sshocked.Services;

public static class ServerMonitorCommands
{
    public const string Linux = """
        die() { printf '%s\n' "$*" >&2; exit 1; }
        [ -r /proc/stat ] && [ -r /proc/meminfo ] || die 'Linux /proc metrics are unavailable.'
        printf 'BOOT %s\n' "$(cat /proc/sys/kernel/random/boot_id)"
        head -n 1 /proc/stat
        awk '/^(MemTotal|MemAvailable|SwapTotal|SwapFree):/ { print $1, $2 }' /proc/meminfo
        printf 'UPTIME %s\n' "$(cut -d ' ' -f 1 /proc/uptime)"
        printf 'FS_BEGIN\n'
        LC_ALL=C df -Pk
        """;

    public const string Windows = """
        $os=Get-CimInstance Win32_OperatingSystem;
        $cpu=Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'";
        $volumes=@(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3'|ForEach-Object{
          [pscustomobject]@{name=$_.DeviceID;size=$_.Size;available=$_.FreeSpace}
        });
        [pscustomobject]@{cpu=$cpu.PercentProcessorTime;total=[double]$os.TotalVisibleMemorySize*1024;
          available=[double]$os.FreePhysicalMemory*1024;uptime=((Get-Date)-$os.LastBootUpTime).TotalSeconds;
          boot=$os.LastBootUpTime.ToUniversalTime().ToString('o');volumes=$volumes}|ConvertTo-Json -Compress -Depth 5
        """;
}
