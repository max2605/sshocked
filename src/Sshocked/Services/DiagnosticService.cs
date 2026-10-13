using System.Runtime.InteropServices;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DiagnosticService(IConfigRepository repository, IDiagnosticToolService tools,
    IHostReachabilityService reachability, IHostResolutionService resolution, IAuditService audit) : IDiagnosticService
{
    public async Task<DiagnosticReport> Create(string? host, DiagnosticOptions options, CancellationToken cancellationToken = default)
    {
        var config = repository.Load();
        var selected = SelectHost(config, host);
        var credentials = config.Hosts.SelectMany(item => new[] { item.SshPassword, item.SudoPassword })
            .Where(value => !string.IsNullOrEmpty(value)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var includePaths = options.IncludePaths && config.Audit.LogFilePaths;
        var includeCommands = options.IncludeCommandText && config.Audit.LogCommandText;
        string Safe(string value) => DiagnosticRedactor.Sanitize(value, credentials, includePaths) ?? string.Empty;
        var report = new DiagnosticReport
        {
            Platform = Safe(RuntimeInformation.OSDescription),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            IncludesPaths = includePaths,
            IncludesCommandText = includeCommands
        };
        if (selected is not null)
        {
            report.Host = new(Safe(selected.Id), Safe(selected.Alias), Safe(selected.HostName), selected.Port, Safe(selected.User),
                selected.AuthType.ToString(), selected.AzureBastion is { } bastion ? $"Azure Bastion {bastion.ConnectionMode}/{bastion.AuthType}"
                    : string.IsNullOrWhiteSpace(selected.ProxyJump) || selected.ProxyJump.Equals("none", StringComparison.OrdinalIgnoreCase) ? "Direct SSH" : "ProxyJump",
                includePaths ? DiagnosticRedactor.Sanitize(selected.IdentityFile, credentials, true) : null,
                selected.KeySecretRef is not null, selected.PasswordSecretRef is not null, selected.SudoPasswordSecretRef is not null,
                DiagnosticRedactor.Sanitize(selected.ProxyJump, credentials, includePaths));
            var status = options.Probe ? await reachability.CheckPortOpen(selected, ct: cancellationToken) : reachability.GetStatus(selected);
            report.Checks.Add(new("route", status.State == HostReachabilityState.Offline ? "failed" : "info",
                $"{status.State}; checked {status.LastCheckedUtc?.ToString("O") ?? "never"}. TCP status does not prove SSH authentication or a routed connection."));
            var dns = resolution.GetResolution(selected.HostName);
            report.Checks.Add(new("dns", dns.State == HostResolutionState.Error ? "failed" : "info",
                $"{dns.State}; addresses: {string.Join(", ", dns.Addresses ?? [])}. {Safe(dns.Error ?? string.Empty)}"));
            if (selected.AuthType == AuthType.SshKey && selected.KeySecretRef is null && !string.IsNullOrEmpty(selected.IdentityFile))
                report.Checks.Add(new("identity-file", File.Exists(selected.IdentityFile) ? "ok" : "failed",
                    File.Exists(selected.IdentityFile) ? "Configured key file exists; contents were not read." : "Configured key file is missing; contents were not read."));
        }
        report.Tools = (await tools.Inspect(includePaths, cancellationToken)).Select(tool => new DiagnosticTool(Safe(tool.Name), tool.Available,
            DiagnosticRedactor.Sanitize(tool.Version, credentials, includePaths), includePaths ? DiagnosticRedactor.Sanitize(tool.Path, credentials, true) : null,
            DiagnosticRedactor.Sanitize(tool.Note, credentials, includePaths))).ToList();
        var nativeAad = selected?.AzureBastion is { ConnectionMode: BastionConnectionMode.Native, AuthType: BastionAuthType.Aad };
        var required = nativeAad ? "az" : "ssh";
        report.Checks.Add(new("required-tool", report.Tools.Any(tool => tool.Name == required && tool.Available) ? "ok" : "failed", $"Required executable: {required}."));
        if (selected?.AzureBastion is not null && !nativeAad)
            report.Checks.Add(new("azure-cli", report.Tools.Any(tool => tool.Name == "az" && tool.Available) ? "ok" : "failed", "Azure Bastion routes also require Azure CLI."));
        report.Checks.Add(new("terminal", report.Tools.Any(tool => tool.Name is "wt" or "osascript" or "x-terminal-emulator" or "gnome-terminal" or "konsole" or "xterm" && tool.Available)
            ? "ok" : "warning", "Terminal availability affects separate terminal sessions; single-session Connect does not require a new terminal."));
        report.Checks.Add(new("askpass", Environment.ProcessPath is not null ? "ok" : "failed", "Application executable is used as the SSH askpass helper; no password or environment data was collected."));
        if (options.IncludePaths && !includePaths || options.IncludeCommandText && !includeCommands)
            report.Checks.Add(new("privacy", "info", "Requested paths or command text were excluded by audit privacy settings."));
        foreach (var entry in audit.Query(new(Last: 200, Server: selected?.Id)).Where(entry => !entry.Success).Take(20))
        {
            var action = entry.Category is "command" or "docker" ? includeCommands ? entry.Action : null
                : entry.Category == "filetransfer" && includePaths ? entry.Action : null;
            report.Failures.Add(new(entry.Timestamp.ToUniversalTime(), Safe(entry.Category), Safe(entry.EventType), entry.ExitCode,
                includeCommands ? DiagnosticRedactor.Sanitize(entry.ErrorMessage, credentials, includePaths) : FailureType(entry.ErrorMessage),
                DiagnosticRedactor.Sanitize(action, credentials, includePaths)));
        }
        return report;
    }

    public void Export(DiagnosticReport report, string path) => ConfigBackupFiles.Write(path,
        JsonSerializer.Serialize(report, DiagnosticJsonContext.Default.DiagnosticReport));

    private static string FailureType(string? message) => message is not null && message.EndsWith("Exception", StringComparison.Ordinal) &&
        message.All(character => char.IsAsciiLetterOrDigit(character) || character == '.') ? message
        : "Failure details omitted by default; inspect sanitized command text only when explicitly requested.";

    private static ServerHost? SelectHost(AppConfig config, string? name)
    {
        if (name is null) return null;
        var matches = config.Hosts.Where(host => host.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || host.Alias.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw new ArgumentException("Host not found or ambiguous; select its stable ID.");
    }
}
