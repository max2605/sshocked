using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public static class BastionConfigurationUi
{
    public static AzureBastionConfig Configure(AzureBastionConfig? existing, string user, ISshKeyPickerService keys)
    {
        var config = existing ?? new AzureBastionConfig();
        config.BastionName = Required("Bastion name", config.BastionName);
        config.BastionResourceGroup = Required("Bastion resource group", config.BastionResourceGroup);
        config.ConnectionMode = AnsiConsole.Prompt(new SelectionPrompt<BastionConnectionMode>().Title("Connection mode:")
            .AddChoices(Enum.GetValues<BastionConnectionMode>()).DefaultValue(config.ConnectionMode));
        config.AuthType = AnsiConsole.Prompt(new SelectionPrompt<BastionAuthType>().Title("VM authentication:")
            .AddChoices(config.ConnectionMode == BastionConnectionMode.Native ? Enum.GetValues<BastionAuthType>() : [BastionAuthType.SshKey, BastionAuthType.Password])
            .DefaultValue(config.ConnectionMode == BastionConnectionMode.Tunnel && config.AuthType == BastionAuthType.Aad ? BastionAuthType.SshKey : config.AuthType));
        var target = config.AuthType == BastionAuthType.Aad ? "VM resource ID" : AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Target:").AddChoices("VM resource ID", "Private IP address").DefaultValue(config.TargetIpAddress is not null ? "Private IP address" : "VM resource ID"));
        if (target == "VM resource ID") { config.TargetResourceId = Required("Full VM resource ID", config.TargetResourceId ?? ""); config.TargetIpAddress = null; }
        else { config.TargetIpAddress = Required("Target private IP address", config.TargetIpAddress ?? ""); config.TargetResourceId = null; }
        config.TargetPort = Port("Target SSH port", config.TargetPort, false);
        if (config.ConnectionMode == BastionConnectionMode.Tunnel) config.LocalPort = Port("Local port (0 = automatic)", config.LocalPort, true);
        config.Username = Required("VM username", config.Username ?? user);
        if (config.AuthType == BastionAuthType.SshKey) config.SshKeyPath = keys.Pick(config.SshKeyPath);
        config.SubscriptionId = Optional("Subscription ID", config.SubscriptionId);
        config.TenantId = Optional("Tenant ID", config.TenantId);
        // Use the same validation as execution so invalid targeting never reaches persistent storage.
        AzureBastionService.Validate(config);
        return config;
    }

    private static string Required(string label, string value) => AnsiConsole.Prompt(new TextPrompt<string>($"[green]{label}[/]:")
        .DefaultValue(value).Validate(s => !string.IsNullOrWhiteSpace(s) ? ValidationResult.Success() : ValidationResult.Error("A value is required."))).Trim();
    private static string? Optional(string label, string? value)
    {
        var result = AnsiConsole.Prompt(new TextPrompt<string>($"[green]{label}[/] (optional):").DefaultValue(value ?? "").AllowEmpty()).Trim();
        return result.Length == 0 ? null : result;
    }
    private static int Port(string label, int value, bool allowZero) => AnsiConsole.Prompt(new TextPrompt<int>($"[green]{label}[/]:")
        .DefaultValue(value).Validate(p => p >= (allowZero ? 0 : 1) && p <= 65535 ? ValidationResult.Success() : ValidationResult.Error("Invalid port.")));
}
