using Spectre.Console;
using Sshocked.Models;

namespace Sshocked.Services;

public static class HostVisuals
{
    public static string AuthBadge(ServerHost host, string theme = "Dark")
    {
        var light = theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        var auth = host.AzureBastion is not null ? AuthType.AzureBastion : host.AuthType;
        var (icon, label, color) = auth switch
        {
            AuthType.Password => ("🔑", "Password", light ? "darkgoldenrod" : "yellow"),
            AuthType.SshAgent => ("🤖", "Agent", light ? "blue" : "deepskyblue1"),
            AuthType.CustomConfig => ("⚙", "Custom", light ? "grey35" : "grey"),
            AuthType.AzureBastion => ("☁", "Azure Bastion", light ? "teal" : "cyan"),
            _ => ("🔐", "SSH Key", light ? "darkgreen" : "green")
        };
        return $"[{color}]{icon} [[{label}]][/]";
    }

    public static string Alias(ServerHost host, string theme = "Dark")
        => (host.IsFavorite ? $"[{(theme == "Light" ? "darkgoldenrod" : "yellow")}]★[/] " : "") + $"[bold]{Markup.Escape(host.Alias)}[/]";

    public static string ReachabilityBadge(HostReachability status) => status.State switch
    {
        HostReachabilityState.Online => "[green]● online[/]",
        HostReachabilityState.Offline => "[red]○ offline[/]",
        HostReachabilityState.Stale => "[yellow]? stale[/]",
        HostReachabilityState.Checking => "[grey]… checking[/]",
        HostReachabilityState.ViaBastion => "[cyan]☁ via Bastion[/]",
        HostReachabilityState.ViaJumpHost => "[cyan]↗ via jump[/]",
        _ => "[grey]? unchecked[/]"
    };

    public static int ContainerCount(ServerHost host)
        => host.SavedContainers.Concat(host.DockerInfo?.Containers ?? [])
            .Select(c => !string.IsNullOrWhiteSpace(c.ContainerId) ? c.ContainerId : c.Name)
            .Distinct(StringComparer.Ordinal).Count();

    public static string DockerBadge(ServerHost host)
    {
        var count = ContainerCount(host);
        if (count > 0) return $"[cyan][[D:{count}]][/]";
        return host.DockerInfo?.DockerAvailable == true ? "[cyan][[D:0]][/]" : "[grey]—[/]";
    }

    public static string RelativeTime(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        if (timestamp is null) return "Never";
        var age = now - timestamp.Value;
        if (age < TimeSpan.FromMinutes(1)) return "Just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m ago";
        if (age < TimeSpan.FromHours(24)) return $"{(int)age.TotalHours}h ago";
        var days = (now.ToLocalTime().Date - timestamp.Value.ToLocalTime().Date).Days;
        if (days == 0) return "Today";
        if (days == 1) return "Yesterday";
        return $"{Math.Max(1, days)}d ago";
    }
}
