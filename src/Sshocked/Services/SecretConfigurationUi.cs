using System.Text.RegularExpressions;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SecretConfigurationUi(IConfigRepository repository, ISecretProviderRegistry registry,
    IEnumerable<ISecretProviderFactory> factories, IMenuFactory menus, IConsoleWriterService console,
    IConsoleHelperService consoleHelper) : ISecretConfigurationUi
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            running = await menus.RunMenu("[bold yellow]Secret Providers[/]",
            [
                new("Add provider", 'A', executeAsync: () => Run(() => { SaveProvider(PromptProvider(null)); return Task.CompletedTask; })),
                new("Edit provider", 'E', executeAsync: () => Run(EditProvider)),
                new("Remove provider", 'R', executeAsync: () => Run(RemoveProvider)),
                new("Set default provider", 'D', executeAsync: () => Run(SetDefault)),
                new("Test provider connection", 'T', executeAsync: () => Run(TestConnection)),
                new("List secret names", 'L', executeAsync: () => Run(ListSecrets)),
                new("Back", 'B', actionType: MenuActionType.Back)
            ]);
        }
    }

    public void ConfigureHost(ServerHost host)
    {
        while (true)
        {
            var action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]Secret references[/]")
                .AddChoices("SSH private key", "SSH password", "Sudo password", "Done"));
            if (action == "Done") return;
            if (action == "SSH private key")
            {
                host.KeySecretRef = Pick(SecretType.SshPrivateKey, host.KeySecretRef);
                if (host.KeySecretRef != null) { host.PasswordSecretRef = null; host.IdentityFile = null; if (host.AzureBastion == null) host.AuthType = AuthType.SshKey; }
            }
            else if (action == "SSH password")
            {
                host.PasswordSecretRef = Pick(SecretType.Password, host.PasswordSecretRef);
                if (host.PasswordSecretRef != null) { host.KeySecretRef = null; if (host.AzureBastion == null) host.AuthType = AuthType.Password; }
            }
            else host.SudoPasswordSecretRef = Pick(SecretType.Password, host.SudoPasswordSecretRef);
        }
    }

    public SecretRef? Pick(SecretType type, SecretRef? current = null)
    {
        var action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title($"[green]{type} secret[/]")
            .AddChoices("Keep current reference", "No secret reference", "Choose from provider", "Enter secret identifier"));
        if (action == "Keep current reference") return current;
        if (action == "No secret reference") return null;
        var config = ChooseProvider();
        if (config == null) return current;
        var actualType = type;
        if (type == SecretType.SshPrivateKey)
            actualType = AnsiConsole.Prompt(new SelectionPrompt<SecretType>().Title("[green]Private key format[/]")
                .AddChoices(SecretType.SshPrivateKey, SecretType.Certificate).DefaultValue(current?.SecretType ?? SecretType.SshPrivateKey));
        string id;
        if (action == "Choose from provider")
        {
            try
            {
                var secrets = registry.GetProvider(config.Name).ListSecretsAsync(actualType).GetAwaiter().GetResult();
                if (secrets.Count == 0) { console.WriteLine("No matching secret names found. Tag secrets with sshocked-type or enter their identifier manually."); return current; }
                id = AnsiConsole.Prompt(new SelectionPrompt<SecretMetadata>().Title("[green]Secret name[/]")
                    .UseConverter(secret => Markup.Escape(secret.Name)).AddChoices(secrets)).Id;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { console.WriteLine("Cannot list secret names. Verify Azure login and list permissions, or enter the identifier manually."); return current; }
        }
        else id = AnsiConsole.Prompt(new TextPrompt<string>("[green]Secret name or versioned secret URI[/]:")
            .DefaultValue(current?.SecretId ?? string.Empty).Validate(value => !string.IsNullOrWhiteSpace(value) && !value.Contains('\0')
                ? ValidationResult.Success() : ValidationResult.Error("[red]Enter a secret name or URI.[/]"))).Trim();
        return new SecretRef { ProviderName = config.Name, SecretId = id, SecretType = actualType };
    }

    private async Task Run(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { console.WriteLine("Secret provider operation failed. Verify provider settings, Azure credentials, and Key Vault permissions."); }
        consoleHelper.WaitForKey();
    }

    private SecretProviderConfig? ChooseProvider()
    {
        var configs = registry.GetConfigurations();
        if (configs.Count == 0) { console.WriteLine("Configure a secret provider in Settings first."); return null; }
        return AnsiConsole.Prompt(new SelectionPrompt<SecretProviderConfig>().Title("[green]Secret provider[/]")
            .UseConverter(config => Markup.Escape(config.Name + (config.IsDefault ? " (default)" : ""))).AddChoices(configs));
    }

    private SecretProviderConfig PromptProvider(SecretProviderConfig? current)
    {
        var names = registry.GetConfigurations().Where(config => !config.Name.Equals(current?.Name, StringComparison.OrdinalIgnoreCase)).Select(config => config.Name).ToList();
        var name = AnsiConsole.Prompt(new TextPrompt<string>("[green]Provider name[/]:").DefaultValue(current?.Name ?? string.Empty)
            .Validate(value => Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$") && !names.Contains(value, StringComparer.OrdinalIgnoreCase)
                ? ValidationResult.Success() : ValidationResult.Error("[red]Use a unique name with letters, digits, dots, hyphens or underscores.[/]")));
        var type = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]Provider type[/]")
            .AddChoices(factories.Select(factory => factory.ProviderType).Distinct(StringComparer.OrdinalIgnoreCase))
            .DefaultValue(current?.ProviderType ?? "AzureKeyVault"));
        var vault = AnsiConsole.Prompt(new TextPrompt<string>("[green]Key Vault URI[/] (https://name.vault.azure.net/):")
            .DefaultValue(current?.VaultUri ?? string.Empty).Validate(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
                ? ValidationResult.Success() : ValidationResult.Error("[red]Enter an HTTPS vault root URI.[/]")));
        console.WriteLine("Use az login, managed identity, or Azure environment credentials. Client secrets/certificates are supplied through environment variables.");
        return new SecretProviderConfig
        {
            Name = name, ProviderType = type, VaultUri = vault,
            TenantId = Optional("Tenant ID", current?.TenantId), ClientId = Optional("Client/managed identity ID", current?.ClientId),
            CacheTtlSeconds = AnsiConsole.Prompt(new TextPrompt<int>("[green]Memory cache TTL in seconds[/] (0 disables cache):")
                .DefaultValue(current?.CacheTtlSeconds ?? 60).Validate(value => value is >= 0 and <= 3600 ? ValidationResult.Success() : ValidationResult.Error("[red]Use 0 to 3600 seconds.[/]"))),
            IsDefault = AnsiConsole.Confirm("Make this the default secret provider?", current?.IsDefault ?? !registry.GetConfigurations().Any(c => c.IsDefault))
        };
    }

    private void SaveProvider(SecretProviderConfig settings, string? previousName = null)
    {
        repository.Update(config =>
        {
            if (previousName != null)
            {
                config.SecretProviders.RemoveAll(provider => provider.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase));
                foreach (var reference in config.Hosts.SelectMany(References).Where(reference => reference?.ProviderName?.Equals(previousName, StringComparison.OrdinalIgnoreCase) == true)) reference!.ProviderName = settings.Name;
            }
            if (settings.IsDefault) foreach (var provider in config.SecretProviders) provider.IsDefault = false;
            config.SecretProviders.Add(settings);
        });
        registry.ClearCache();
        console.WriteLine("Provider settings saved.");
    }

    private Task EditProvider()
    {
        var provider = ChooseProvider();
        if (provider != null) SaveProvider(PromptProvider(provider), provider.Name);
        return Task.CompletedTask;
    }

    private Task RemoveProvider()
    {
        var provider = ChooseProvider();
        if (provider == null) return Task.CompletedTask;
        var effectiveDefault = provider.IsDefault || registry.GetConfigurations().Count == 1;
        var dependent = repository.Load().Hosts.Count(host => References(host).Any(reference => ReferencesProvider(reference, provider, effectiveDefault)));
        if (!AnsiConsole.Confirm($"Remove provider '{Markup.Escape(provider.Name)}' and clear its references on {dependent} host(s)?", false)) return Task.CompletedTask;
        repository.Update(config =>
        {
            config.SecretProviders.RemoveAll(p => p.Name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var host in config.Hosts)
            {
                if (ReferencesProvider(host.KeySecretRef, provider, effectiveDefault)) host.KeySecretRef = null;
                if (ReferencesProvider(host.PasswordSecretRef, provider, effectiveDefault)) host.PasswordSecretRef = null;
                if (ReferencesProvider(host.SudoPasswordSecretRef, provider, effectiveDefault)) host.SudoPasswordSecretRef = null;
            }
        });
        registry.ClearCache();
        console.WriteLine("Provider removed.");
        return Task.CompletedTask;
    }

    private Task SetDefault()
    {
        var provider = ChooseProvider();
        if (provider != null) repository.Update(config => { foreach (var item in config.SecretProviders) item.IsDefault = item.Name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase); });
        registry.ClearCache();
        return Task.CompletedTask;
    }

    private async Task TestConnection()
    {
        var provider = ChooseProvider();
        if (provider != null) console.WriteLine(await registry.GetProvider(provider.Name).TestConnectionAsync()
            ? "Provider connection succeeded." : "Connection failed. Verify Azure login and Key Vault list permissions.");
    }

    private async Task ListSecrets()
    {
        var provider = ChooseProvider();
        if (provider == null) return;
        var secrets = await registry.GetProvider(provider.Name).ListSecretsAsync();
        foreach (var secret in secrets) console.WriteLine($"{secret.Name} ({secret.SecretType?.ToString() ?? "untyped"})");
        console.WriteLine($"{secrets.Count} secret name(s).");
    }

    private static IEnumerable<SecretRef?> References(ServerHost host) => [host.KeySecretRef, host.PasswordSecretRef, host.SudoPasswordSecretRef];
    private static bool ReferencesProvider(SecretRef? reference, SecretProviderConfig provider, bool effectiveDefault) => reference != null &&
        (reference.ProviderName == null ? effectiveDefault : reference.ProviderName.Equals(provider.Name, StringComparison.OrdinalIgnoreCase));
    private static string? Optional(string label, string? value)
    {
        var result = AnsiConsole.Prompt(new TextPrompt<string>($"[green]{Markup.Escape(label)}[/] (optional):").DefaultValue(value ?? string.Empty).AllowEmpty()).Trim();
        return result.Length == 0 ? null : result;
    }
}
