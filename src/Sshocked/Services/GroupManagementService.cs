using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupManagementService(IConfigRepository configRepository, ILogger<GroupManagementService> logger) : IGroupManagementService
{
    public List<ServerGroup> GetAll()
    {
        var config = configRepository.Load();
        return config.Groups;
    }

    public ServerGroup Create(string name)
    {
        var group = new ServerGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name
        };

        configRepository.Update(config => config.Groups.Add(group));
        logger.LogInformation("Created group '{GroupName}' ({GroupId})", name, group.Id);

        return group;
    }

    public void Rename(string groupId, string newName)
    {
        var renamed = false;
        configRepository.Update(config =>
        {
            var group = config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is null) return;
            group.Name = newName;
            renamed = true;
        });
        if (!renamed)
        {
            logger.LogWarning("Attempted to rename non-existent group {GroupId}", groupId);
            return;
        }
        logger.LogInformation("Renamed group {GroupId} to '{NewName}'", groupId, newName);
    }

    public void Delete(string groupId)
    {
        string? name = null;
        configRepository.Update(config =>
        {
            var group = config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is null) return;
            name = group.Name;
            config.Groups.Remove(group);
            foreach (var host in config.Hosts.Where(h => h.GroupId == groupId)) host.GroupId = string.Empty;
            foreach (var container in config.Hosts.SelectMany(h => h.SavedContainers).Where(c => c.GroupId == groupId))
                container.GroupId = string.Empty;
        });
        if (name is null)
        {
            logger.LogWarning("Attempted to delete non-existent group {GroupId}", groupId);
            return;
        }
        logger.LogInformation("Deleted group '{GroupName}' ({GroupId})", name, groupId);
    }

    public void AssignHost(string hostId, string groupId)
    {
        string? alias = null;
        configRepository.Update(config =>
        {
            var host = config.Hosts.FirstOrDefault(h => h.Id == hostId);
            if (host is null) return;
            // A group selected in another menu can have been removed in the meantime.
            host.GroupId = string.IsNullOrEmpty(groupId) || config.Groups.Any(g => g.Id == groupId) ? groupId : string.Empty;
            alias = host.Alias;
        });
        if (alias is null)
        {
            logger.LogWarning("Attempted to assign non-existent host {HostId}", hostId);
            return;
        }
        logger.LogInformation("Assigned host '{Alias}' to group {GroupId}", alias, groupId);
    }
}
