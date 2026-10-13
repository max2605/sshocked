using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerFormService(IGroupManagementService groups, ISshKeyPickerService keyPicker,
    ISecretConfigurationUi secrets, IPathInputService paths, IServerTagService tagsService,
    LocalCredentialUi? localCredentials = null) : IServerFormService
{
    public ServerHost? Fill(AppConfig config, ServerHost? existing = null)
    {
        var aliasPrompt = new TextPrompt<string>(existing is null
            ? "[green]Alias[/] (or [grey]leave empty to cancel[/]):" : "[green]Alias[/]:");
        if (existing is null) aliasPrompt.AllowEmpty();
        else aliasPrompt.DefaultValue(existing.Alias);
        var alias = AnsiConsole.Prompt(aliasPrompt.Validate(input => ValidateAlias(config, existing, input)));
        if (string.IsNullOrWhiteSpace(alias)) return null;

        var hostName = PromptRequired("[green]HostName / IP[/]:", existing?.HostName, "HostName cannot be empty.");
        var user = AnsiConsole.Prompt(new TextPrompt<string>("[green]User[/]:").DefaultValue(existing?.User ?? Environment.UserName));
        var port = AnsiConsole.Prompt(new TextPrompt<int>("[green]Port[/]:").DefaultValue(existing?.Port ?? 22)
            .Validate(value => value is < 1 or > 65535
                ? ValidationResult.Error("[red]Port must be between 1 and 65535.[/]") : ValidationResult.Success()));
        var availableGroups = groups.GetAll();
        var groupPrompt = new SelectionPrompt<string>().Title("[green]Group[/]:").UseConverter(Markup.Escape)
            .AddChoices(availableGroups.Select(group => group.Name).Prepend(ServerFormLabels.NoGroup));
        if (existing is not null)
            groupPrompt.DefaultValue(availableGroups.FirstOrDefault(group => group.Id == existing.GroupId)?.Name ?? ServerFormLabels.NoGroup);
        var groupName = AnsiConsole.Prompt(groupPrompt);
        var tagsPrompt = new TextPrompt<string>("[green]Tags[/] (comma-separated, '-' clears all):");
        if (existing is null) tagsPrompt.AllowEmpty();
        else tagsPrompt.DefaultValue(string.Join(", ", existing.Tags));
        var tags = AnsiConsole.Prompt(tagsPrompt);

        var host = existing ?? new ServerHost { Id = Guid.NewGuid().ToString("N") };
        var originalAuth = host.AuthType;
        var originalIdentity = host.IdentityFile;
        var originalBastionAuth = host.AzureBastion?.AuthType;
        var authentication = PickAuthentication(existing);
        var identity = host.IdentityFile;
        if (authentication == ServerFormLabels.SshKey) identity = keyPicker.Pick(existing?.IdentityFile);
        var options = host.CustomSshOptions;
        if (authentication == ServerFormLabels.CustomOptions)
        {
            options = PromptRequired("[green]Custom SSH Options[/] (e.g. -o StrictHostKeyChecking=no -o ServerAliveInterval=60):",
                existing is null ? null : existing.CustomSshOptions ?? string.Empty, "Custom SSH options cannot be empty.");
        }

        host.Alias = alias.Trim();
        host.HostName = hostName.Trim();
        host.User = user.Trim();
        host.Port = port;
        host.GroupId = groupName == ServerFormLabels.NoGroup ? string.Empty : availableGroups.First(group => group.Name == groupName).Id;
        host.Tags = tagsService.Normalize(tags.Trim() == "-" ? []
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        host.AuthType = authentication switch
        {
            ServerFormLabels.Password => AuthType.Password,
            ServerFormLabels.SshAgent => AuthType.SshAgent,
            ServerFormLabels.CustomOptions => AuthType.CustomConfig,
            ServerFormLabels.AzureBastion => AuthType.AzureBastion,
            _ => AuthType.SshKey
        };
        host.IdentityFile = Normalize(identity);
        host.CustomSshOptions = Normalize(options);
        host.AzureBastion = authentication == ServerFormLabels.AzureBastion
            ? BastionConfigurationUi.Configure(host.AzureBastion, user, keyPicker) : null;
        if (existing is not null && authentication != ServerFormLabels.KeyVault
            && (originalAuth != host.AuthType || originalIdentity != host.IdentityFile || originalBastionAuth != host.AzureBastion?.AuthType))
        {
            host.KeySecretRef = null;
            host.PasswordSecretRef = null;
        }
        var hasSecrets = host.KeySecretRef is not null || host.PasswordSecretRef is not null || host.SudoPasswordSecretRef is not null;
        if (authentication == ServerFormLabels.KeyVault || AnsiConsole.Confirm("Configure secret references for this host?", hasSecrets))
            secrets.ConfigureHost(host);
        if (AnsiConsole.Confirm("Configure connection hooks?", host.Hooks is not null))
            host.Hooks = HookConfigurationUi.Configure(host.Hooks, paths);
        host.RecordSession = SessionRecordingConfigurationUi.ConfigureHost(host.RecordSession);
        localCredentials?.Configure(host);
        return host;
    }

    private static string PickAuthentication(ServerHost? existing)
    {
        var prompt = new SelectionPrompt<string>().Title("[green]Authentication Method[/]:")
            .AddChoices(ServerFormLabels.AuthenticationMethods);
        if (existing is not null)
        {
            prompt.DefaultValue(existing.AuthType switch
            {
                AuthType.Password => ServerFormLabels.Password,
                AuthType.SshAgent => ServerFormLabels.SshAgent,
                AuthType.CustomConfig => ServerFormLabels.CustomOptions,
                AuthType.AzureBastion => ServerFormLabels.AzureBastion,
                _ => ServerFormLabels.SshKey
            });
        }
        return AnsiConsole.Prompt(prompt);
    }

    private static ValidationResult ValidateAlias(AppConfig config, ServerHost? existing, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return existing is null ? ValidationResult.Success() : ValidationResult.Error("[red]Alias cannot be empty.[/]");
        return config.Hosts.Any(host => host.Id != existing?.Id && host.Alias.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase))
            ? ValidationResult.Error("[red]Alias already exists.[/]") : ValidationResult.Success();
    }

    private static string PromptRequired(string label, string? defaultValue, string error)
    {
        var prompt = new TextPrompt<string>(label);
        if (defaultValue is not null) prompt.DefaultValue(defaultValue);
        return AnsiConsole.Prompt(prompt.Validate(input => string.IsNullOrWhiteSpace(input)
            ? ValidationResult.Error($"[red]{error}[/]") : ValidationResult.Success()));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
