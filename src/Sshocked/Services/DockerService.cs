using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DockerService(IProcessService processService, IConfigRepository configRepository, ISecretProviderRegistry secrets, ILogger<DockerService> logger) : IDockerService
{
    public async Task<DockerHostInfo> Scan(ServerHost host)
    {
        logger.LogInformation("Scanning host {Alias} for Docker containers", host.Alias);

        var info = new DockerHostInfo();

        // Password auth doesn't support non-interactive remote commands
        if (host.AuthType == AuthType.Password && host.PasswordSecretRef is null)
        {
            logger.LogInformation("Skipping Docker scan for {Alias}: password auth not supported for non-interactive commands", host.Alias);
            info.DockerAvailable = false;
            info.LastScannedAt = DateTime.UtcNow;
            CacheAndSave(host, info);
            return info;
        }

        var sudo = host.SudoPasswordSecretRef is null ? BuildSudoPrefix(host.SudoPassword) : "sudo -S -p '' ";
        async Task<string> RunScanCommand(string command)
        {
            if (host.SudoPasswordSecretRef is null) return await processService.RunSshCommand(host, command);
            var secret = await secrets.ResolveAsync(host.SudoPasswordSecretRef);
            if (!secret.Success || secret.Value is null) throw new InvalidOperationException(secret.ErrorMessage ?? "Could not resolve sudo password.");
            var result = await processService.RunSshCommandWithInput(host, command, secret.Value + "\n");
            result.EnsureSuccess("Docker scan");
            return result.StandardOutput;
        }

        var dockerInfoOutput = await RunScanCommand($"{sudo}docker info --format '{{{{json .}}}}' 2>/dev/null || echo 'DOCKER_NOT_AVAILABLE'");
        dockerInfoOutput = dockerInfoOutput.Trim();

        if (dockerInfoOutput == "DOCKER_NOT_AVAILABLE" || string.IsNullOrEmpty(dockerInfoOutput))
        {
            logger.LogInformation("Docker is not available on host {Alias}", host.Alias);
            info.DockerAvailable = false;
            info.LastScannedAt = DateTime.UtcNow;
            CacheAndSave(host, info);
            return info;
        }

        info.DockerAvailable = true;

        try
        {
            using var doc = JsonDocument.Parse(dockerInfoOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("ServerVersion", out var version))
                info.ServerVersion = version.GetString();

            if (root.TryGetProperty("OSType", out var osType))
                info.OsType = osType.GetString();

            if (root.TryGetProperty("Architecture", out var arch))
                info.Architecture = arch.GetString();

            if (root.TryGetProperty("ContainersRunning", out var running))
                info.ContainersRunning = running.GetInt32();

            if (root.TryGetProperty("Containers", out var total))
                info.ContainersTotal = total.GetInt32();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse docker info JSON from {Alias}", host.Alias);
        }

        var containersOutput = await RunScanCommand($"{sudo}docker ps -a --format '{{{{json .}}}}' 2>/dev/null");
        info.Containers = ParseContainerJson(containersOutput);

        var composeOutput = await RunScanCommand($"{sudo}docker compose ls --format '{{{{json .}}}}' 2>/dev/null || true");
        info.ComposeStacks = ParseComposeStackJson(composeOutput);

        info.LastScannedAt = DateTime.UtcNow;
        CacheAndSave(host, info);

        logger.LogInformation("Scan complete for {Alias}: {Count} containers, {Stacks} compose stacks",
            host.Alias, info.Containers.Count, info.ComposeStacks.Count);

        return info;
    }

    public Task Exec(ServerHost host, ContainerModel container)
    {
        var sudo = BuildSudoPrefix(host.SudoPassword);
        var command = $"{sudo}docker exec -it {container.Name} sh";
        return processService.RunSshInteractive(host, command);
    }

    public Task Logs(ServerHost host, ContainerModel container)
    {
        var sudo = BuildSudoPrefix(host.SudoPassword);
        var command = $"{sudo}docker logs -f {container.Name}";
        return processService.RunSshInteractive(host, command);
    }

    private List<ContainerModel> ParseContainerJson(string raw)
    {
        var containers = new List<ContainerModel>();

        if (string.IsNullOrWhiteSpace(raw))
            return containers;

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var container = new ContainerModel
                {
                    ContainerId = root.TryGetProperty("ID", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Name = root.TryGetProperty("Names", out var names) ? names.GetString() ?? string.Empty : string.Empty,
                    Image = root.TryGetProperty("Image", out var image) ? image.GetString() ?? string.Empty : string.Empty,
                    Status = root.TryGetProperty("Status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                    Ports = root.TryGetProperty("Ports", out var ports) ? ports.GetString() : null,
                    ComposeProject = root.TryGetProperty("ComposeProject", out var project) ? project.GetString() : null
                };

                if (container.Name.StartsWith('/'))
                    container.Name = container.Name[1..];

                containers.Add(container);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse container JSON line: {Line}", trimmed);
            }
        }

        return containers;
    }

    private List<ComposeStackModel> ParseComposeStackJson(string raw)
    {
        var stacks = new List<ComposeStackModel>();

        if (string.IsNullOrWhiteSpace(raw))
            return stacks;

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var stack = new ComposeStackModel
                {
                    Name = root.TryGetProperty("Name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Status = root.TryGetProperty("Status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                    ConfigFiles = root.TryGetProperty("ConfigFiles", out var config) ? config.GetString() : null
                };

                stacks.Add(stack);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse compose stack JSON line: {Line}", trimmed);
            }
        }

        return stacks;
    }

    private void CacheAndSave(ServerHost host, DockerHostInfo info)
    {
        var config = configRepository.Load();

        var existing = config.Hosts.FirstOrDefault(h => h.Id == host.Id);
        if (existing is not null)
        {
            existing.DockerInfo = info;
            configRepository.Save(config);
        }
    }

    private static string BuildSudoPrefix(string? sudoPassword)
    {
        if (string.IsNullOrEmpty(sudoPassword))
            return "sudo ";

        var escaped = sudoPassword.Replace("'", "'\\''");
        return $"echo '{escaped}' | sudo -S ";
    }
}
