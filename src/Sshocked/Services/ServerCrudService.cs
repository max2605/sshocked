using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerCrudService(IConfigRepository repository, IServerFormService form,
    ILogger<ServerCrudService> logger, ILocalCredentialService? localCredentials = null) : IServerCrudService
{
    public ServerHost? Add()
    {
        var host = form.Fill(repository.Load());
        if (host is null)
        {
            logger.LogInformation("User cancelled add server");
            return null;
        }
        repository.Update(config =>
        {
            ValidateUniqueAlias(config, host);
            config.Hosts.Add(host);
        });
        logger.LogInformation("Added host '{Alias}' ({HostName})", host.Alias, host.HostName);
        return host;
    }

    public void Edit(ServerHost host)
    {
        var config = repository.Load();
        var existing = config.Hosts.FirstOrDefault(current => current.Id == host.Id);
        if (existing is null) return;
        var updated = form.Fill(config, existing);
        if (updated is null) return;
        repository.Update(current =>
        {
            var target = current.Hosts.FirstOrDefault(candidate => candidate.Id == updated.Id)
                ?? throw new InvalidOperationException("This host was deleted in another window.");
            ValidateUniqueAlias(current, updated);
            CopyEditableFields(updated, target);
        });
        logger.LogInformation("Edited host '{Alias}'", updated.Alias);
    }

    public bool Delete(ServerHost host)
    {
        if (!AnsiConsole.Confirm($"Are you sure you want to delete '[yellow]{Markup.Escape(host.Alias)}[/]'?")) return false;
        repository.Update(config => config.Hosts.RemoveAll(current => current.Id == host.Id));
        localCredentials?.ClearPassword(host);
        logger.LogInformation("Deleted host '{Alias}'", host.Alias);
        return true;
    }

    private static void ValidateUniqueAlias(AppConfig config, ServerHost host)
    {
        if (config.Hosts.Any(current => current.Id != host.Id && current.Alias.Equals(host.Alias, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This alias was added in another window. Choose another alias.");
    }

    private static void CopyEditableFields(ServerHost source, ServerHost target)
    {
        target.Alias = source.Alias;
        target.HostName = source.HostName;
        target.User = source.User;
        target.Port = source.Port;
        target.GroupId = source.GroupId;
        target.Tags = source.Tags;
        target.AuthType = source.AuthType;
        target.IdentityFile = source.IdentityFile;
        target.CustomSshOptions = source.CustomSshOptions;
        target.AzureBastion = source.AzureBastion;
        target.KeySecretRef = source.KeySecretRef;
        target.PasswordSecretRef = source.PasswordSecretRef;
        target.SudoPasswordSecretRef = source.SudoPasswordSecretRef;
        target.Hooks = source.Hooks;
        target.RecordSession = source.RecordSession;
    }
}
