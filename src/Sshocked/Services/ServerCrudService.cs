using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerCrudService(IConfigRepository configRepository, IGroupManagementService groupManagement, ISshKeyPickerService keyPicker, ISecretConfigurationUi secretUi, ILogger<ServerCrudService> logger) : IServerCrudService
{

    public ServerHost? Add()
    {
        var config = configRepository.Load();

        var alias = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Alias[/] (or [grey]leave empty to cancel[/]):")
                .AllowEmpty()
                .Validate(input =>
                {
                    if (string.IsNullOrWhiteSpace(input))
                        return ValidationResult.Success();
                    if (config.Hosts.Any(h => h.Alias.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase)))
                        return ValidationResult.Error("[red]Alias already exists.[/]");
                    return ValidationResult.Success();
                }));

        if (string.IsNullOrWhiteSpace(alias))
        {
            logger.LogInformation("User cancelled add server");
            return null;
        }

        var hostName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]HostName / IP[/]:")
                .Validate(input =>
                {
                    if (string.IsNullOrWhiteSpace(input))
                        return ValidationResult.Error("[red]HostName cannot be empty.[/]");
                    return ValidationResult.Success();
                }));

        var user = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]User[/]:")
                .DefaultValue(Environment.UserName));

        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Port[/]:")
                .DefaultValue(22)
                .Validate(input =>
                {
                    if (input < 1 || input > 65535)
                        return ValidationResult.Error("[red]Port must be between 1 and 65535.[/]");
                    return ValidationResult.Success();
                }));

        var groups = groupManagement.GetAll();
        var groupName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Group[/]:")
                .UseConverter(Markup.Escape).AddChoices(groups.Select(g => g.Name).Prepend("(none)")));

        var tagsInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Tags[/] (comma-separated):")
                .AllowEmpty());

        var authType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Authentication Method[/]:")
                .AddChoices([
                    "SSH Key (Default)",
                    "Password",
                    "SSH Agent",
                    "Custom SSH Options",
                    "Azure Bastion",
                    "From Key Vault"
                ]));

        string? identityFile = null;
        if (authType == "SSH Key (Default)")
        {
            identityFile = keyPicker.Pick();
        }

        string? customSshOptions = null;
        if (authType == "Custom SSH Options")
        {
            customSshOptions = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Custom SSH Options[/] (e.g. -o StrictHostKeyChecking=no -o ServerAliveInterval=60):")
                    .Validate(input =>
                    {
                        if (string.IsNullOrWhiteSpace(input))
                            return ValidationResult.Error("[red]Custom SSH options cannot be empty.[/]");
                        return ValidationResult.Success();
                    }));
        }

        var host = new ServerHost
        {
            Id = Guid.NewGuid().ToString("N"),
            Alias = alias.Trim(),
            HostName = hostName.Trim(),
            User = user.Trim(),
            Port = port,
            GroupId = groupName == "(none)" ? string.Empty : groups.First(g => g.Name == groupName).Id,
            Tags = string.IsNullOrWhiteSpace(tagsInput)
                ? []
                : tagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            AuthType = authType switch
            {
                "Password" => Models.AuthType.Password,
                "SSH Agent" => Models.AuthType.SshAgent,
                "Custom SSH Options" => Models.AuthType.CustomConfig,
                "Azure Bastion" => Models.AuthType.AzureBastion,
                _ => Models.AuthType.SshKey
            },
            IdentityFile = string.IsNullOrWhiteSpace(identityFile) ? null : identityFile.Trim(),
            CustomSshOptions = string.IsNullOrWhiteSpace(customSshOptions) ? null : customSshOptions.Trim()
        };

        if (authType == "Azure Bastion") host.AzureBastion = BastionConfigurationUi.Configure(null, user, keyPicker);
        if (authType == "From Key Vault" || AnsiConsole.Confirm("Configure secret references for this host?", false)) secretUi.ConfigureHost(host);
        if (AnsiConsole.Confirm("Configure connection hooks?", false)) host.Hooks = HookConfigurationUi.Configure(host.Hooks);
        host.RecordSession = SessionRecordingConfigurationUi.ConfigureHost(null);
        config.Hosts.Add(host);
        configRepository.Save(config);
        logger.LogInformation("Added host '{Alias}' ({HostName})", host.Alias, host.HostName);

        return host;
    }

    public void Edit(ServerHost host)
    {
        var config = configRepository.Load();
        var existing = config.Hosts.FirstOrDefault(h => h.Id == host.Id);
        if (existing is null) return;

        var alias = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Alias[/]:")
                .DefaultValue(existing.Alias)
                .Validate(input =>
                {
                    if (string.IsNullOrWhiteSpace(input))
                        return ValidationResult.Error("[red]Alias cannot be empty.[/]");
                    if (config.Hosts.Any(h => h.Id != host.Id && h.Alias.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase)))
                        return ValidationResult.Error("[red]Alias already exists.[/]");
                    return ValidationResult.Success();
                }));

        var hostName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]HostName / IP[/]:")
                .DefaultValue(existing.HostName)
                .Validate(input =>
                {
                    if (string.IsNullOrWhiteSpace(input))
                        return ValidationResult.Error("[red]HostName cannot be empty.[/]");
                    return ValidationResult.Success();
                }));

        var user = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]User[/]:")
                .DefaultValue(existing.User));

        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Port[/]:")
                .DefaultValue(existing.Port)
                .Validate(input =>
                {
                    if (input < 1 || input > 65535)
                        return ValidationResult.Error("[red]Port must be between 1 and 65535.[/]");
                    return ValidationResult.Success();
                }));

        var groups = groupManagement.GetAll();
        var currentGroup = groups.FirstOrDefault(g => g.Id == existing.GroupId);
        var groupName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Group[/]:")
                .UseConverter(Markup.Escape).AddChoices(groups.Select(g => g.Name).Prepend("(none)"))
                .DefaultValue(currentGroup?.Name ?? "(none)"));

        var tagsInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Tags[/] (comma-separated):")
                .DefaultValue(string.Join(", ", existing.Tags)));

        var authTypeLabel = existing.AuthType switch
        {
            Models.AuthType.Password => "Password",
            Models.AuthType.SshAgent => "SSH Agent",
            Models.AuthType.CustomConfig => "Custom SSH Options",
            Models.AuthType.AzureBastion => "Azure Bastion",
            _ => "SSH Key (Default)"
        };

        var authType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Authentication Method[/]:")
                .AddChoices([
                    "SSH Key (Default)",
                    "Password",
                    "SSH Agent",
                    "Custom SSH Options",
                    "Azure Bastion",
                    "From Key Vault"
                ])
                .DefaultValue(authTypeLabel));

        string? identityFile = existing.IdentityFile;
        if (authType == "SSH Key (Default)")
        {
            identityFile = keyPicker.Pick(existing.IdentityFile);
        }

        string? customSshOptions = existing.CustomSshOptions;
        if (authType == "Custom SSH Options")
        {
            customSshOptions = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Custom SSH Options[/] (e.g. -o StrictHostKeyChecking=no -o ServerAliveInterval=60):")
                    .DefaultValue(existing.CustomSshOptions ?? string.Empty)
                    .Validate(input =>
                    {
                        if (string.IsNullOrWhiteSpace(input))
                            return ValidationResult.Error("[red]Custom SSH options cannot be empty.[/]");
                        return ValidationResult.Success();
                    }));
        }

        existing.Alias = alias.Trim();
        existing.HostName = hostName.Trim();
        existing.User = user.Trim();
        existing.Port = port;
        existing.GroupId = groupName == "(none)" ? string.Empty : groups.First(g => g.Name == groupName).Id;
        existing.Tags = string.IsNullOrWhiteSpace(tagsInput)
            ? []
            : tagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var originalAuth = existing.AuthType;
        var originalIdentity = existing.IdentityFile;
        existing.AuthType = authType switch
        {
            "Password" => Models.AuthType.Password,
            "SSH Agent" => Models.AuthType.SshAgent,
            "Custom SSH Options" => Models.AuthType.CustomConfig,
            "Azure Bastion" => Models.AuthType.AzureBastion,
            _ => Models.AuthType.SshKey
        };
        existing.IdentityFile = string.IsNullOrWhiteSpace(identityFile) ? null : identityFile.Trim();
        existing.CustomSshOptions = string.IsNullOrWhiteSpace(customSshOptions) ? null : customSshOptions.Trim();
        existing.AzureBastion = authType == "Azure Bastion" ? BastionConfigurationUi.Configure(existing.AzureBastion, user, keyPicker) : null;
        if (authType != "From Key Vault" && (originalAuth != existing.AuthType || originalIdentity != existing.IdentityFile))
        {
            existing.KeySecretRef = null;
            existing.PasswordSecretRef = null;
        }
        if (authType == "From Key Vault" || AnsiConsole.Confirm("Configure secret references for this host?",
            existing.KeySecretRef is not null || existing.PasswordSecretRef is not null || existing.SudoPasswordSecretRef is not null)) secretUi.ConfigureHost(existing);

        if (AnsiConsole.Confirm("Configure connection hooks?", existing.Hooks is not null)) existing.Hooks = HookConfigurationUi.Configure(existing.Hooks);
        existing.RecordSession = SessionRecordingConfigurationUi.ConfigureHost(existing.RecordSession);
        configRepository.Save(config);
        logger.LogInformation("Edited host '{Alias}'", existing.Alias);
    }

    public bool Delete(ServerHost host)
    {
        var confirmed = AnsiConsole.Confirm(
            $"Are you sure you want to delete '[yellow]{Markup.Escape(host.Alias)}[/]'?");

        if (!confirmed) return false;

        var config = configRepository.Load();
        config.Hosts.RemoveAll(h => h.Id == host.Id);
        configRepository.Save(config);
        logger.LogInformation("Deleted host '{Alias}'", host.Alias);

        return true;
    }
}


