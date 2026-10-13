namespace Sshocked.Services;

internal static class DockerMonitoringCommands
{
    public const string Info = "docker info --format '{\"ID\":{{json .ID}},\"OSType\":{{json .OSType}}}'";
    public const string Discovery = "docker ps -a --no-trunc --format '{\"Id\":{{json .ID}},\"Name\":{{json .Names}},\"Project\":{{json (.Label \"com.docker.compose.project\")}},\"Service\":{{json (.Label \"com.docker.compose.service\")}}}'";
    public const string InspectTemplate = "{\"Id\":{{json .Id}},\"Name\":{{json .Name}},\"Status\":{{json .State.Status}},\"Running\":{{json .State.Running}},\"ExitCode\":{{json .State.ExitCode}},\"OOMKilled\":{{json .State.OOMKilled}},\"RestartCount\":{{json .RestartCount}},\"Health\":{{if .State.Health}}{{json .State.Health.Status}}{{else}}null{{end}},\"MemoryLimit\":{{json .HostConfig.Memory}},\"Project\":{{json (index .Config.Labels \"com.docker.compose.project\")}},\"Service\":{{json (index .Config.Labels \"com.docker.compose.service\")}}}";

    public static string Inspect(IEnumerable<string> ids) => "docker container inspect --format " + RemoteShell.Quote(InspectTemplate) + " -- " + string.Join(' ', ids.Select(RemoteShell.Quote));
    public static string Stats(IEnumerable<string> ids) => "docker stats --no-stream --no-trunc --format '{{json .}}' -- " + string.Join(' ', ids.Select(RemoteShell.Quote));
}
