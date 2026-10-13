using System.Text.Json;

namespace Sshocked.Models;

public sealed class BatchTarget
{
    private readonly string _snapshot;
    private readonly string? _password;
    private readonly string? _sudoPassword;
    public string HostId { get; }
    public string Alias { get; }
    public string? LocalPath { get; }

    public BatchTarget(ServerHost host, string? localPath = null)
    {
        _snapshot = JsonSerializer.Serialize(host, AppConfigContext.Default.ServerHost);
        _password = host.SshPassword;
        _sudoPassword = host.SudoPassword;
        HostId = host.Id;
        Alias = host.Alias;
        LocalPath = localPath;
    }

    internal ServerHost CreateHost()
    {
        var host = JsonSerializer.Deserialize(_snapshot, AppConfigContext.Default.ServerHost)!;
        host.SshPassword = _password;
        host.SudoPassword = _sudoPassword;
        return host;
    }
}
