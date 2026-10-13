using Sshocked.Models;

namespace Sshocked.Services;

internal static class ConfigBackupValidation
{
    public static List<string> Conflicts(AppConfig config)
    {
        var errors = new List<string>();
        Unique(config.Hosts.Select(host => host.Id), "host IDs", errors);
        Unique(config.Hosts.Select(host => host.Alias), "host aliases", errors);
        Unique(config.Groups.Select(group => group.Id), "group IDs", errors);
        Unique(config.Groups.Select(group => group.Name), "group names", errors);
        Unique(config.Macros.Select(macro => macro.Id), "macro IDs", errors);
        Unique(config.Macros.Select(macro => macro.Name), "macro names", errors);
        Unique(config.TargetViews.Select(view => view.Id), "view IDs", errors);
        Unique(config.TargetViews.Select(view => view.Name), "view names", errors);
        Unique(config.SecretProviders.Select(provider => provider.Name), "secret provider names", errors);
        Unique(config.Monitoring.Monitors.Select(monitor => monitor.Id), "monitor IDs", errors);
        Unique(config.ForwardingProfiles.Select(profile => profile.Id), "forwarding IDs", errors);
        Unique(config.ForwardingProfiles.Select(profile => profile.Name), "forwarding names", errors);
        var groups = config.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var hosts = config.Hosts.Select(host => host.Id).ToHashSet(StringComparer.Ordinal);
        if (config.Hosts.Any(host => string.IsNullOrWhiteSpace(host.HostName) || string.IsNullOrWhiteSpace(host.User) || host.Port is < 1 or > 65535))
            errors.Add("Every host requires a hostname, user and a port from 1 to 65535.");
        if (config.Hosts.Any(host => !string.IsNullOrEmpty(host.GroupId) && !groups.Contains(host.GroupId)))
            errors.Add("A host references a group missing from the restored configuration.");
        if (config.Monitoring.Monitors.Any(monitor => !hosts.Contains(monitor.HostId)))
            errors.Add("A monitor references a host missing from the restored configuration.");
        if (config.ForwardingProfiles.Any(profile => !hosts.Contains(profile.HostId)))
            errors.Add("A forwarding profile references a host missing from the restored configuration.");
        foreach (var profile in config.ForwardingProfiles)
        {
            try { ForwardingValidation.Validate(profile); }
            catch (ArgumentException) { errors.Add("A forwarding profile has an invalid bind address, port or target."); }
        }
        foreach (var monitor in config.Monitoring.Monitors)
        {
            try { MonitoringValidation.Validate(monitor); }
            catch (ArgumentException) { errors.Add("A monitor has an invalid schedule, option or alarm rule."); }
        }
        foreach (var view in config.TargetViews)
        {
            try { _ = TargetViewExpression.Compile(view.Expression); }
            catch (ArgumentException) { errors.Add("A target view has an invalid tag expression."); }
        }
        foreach (var macro in config.Macros)
        {
            try { _ = MacroCommandPolicy.NormalizeName(macro.Name); }
            catch (ArgumentException) { errors.Add("A macro has an invalid name."); }
            if (macro.Commands.Count is < 1 or > MacroCommandPolicy.MaximumCommands || macro.Commands.Any(command =>
                string.IsNullOrWhiteSpace(command) || command.Contains('\0') || command.Length > MacroCommandPolicy.MaximumCommandLength))
                errors.Add("A macro has an invalid command sequence.");
        }
        return errors;
    }

    private static void Unique(IEnumerable<string> values, string label, ICollection<string> errors)
    {
        var entries = values.ToList();
        if (entries.Any(value => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)))
            errors.Add($"The configuration contains empty or invalid {label}.");
        if (entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            errors.Add($"The configuration contains duplicate {label}.");
    }
}
