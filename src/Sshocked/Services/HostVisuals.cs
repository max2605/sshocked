using Spectre.Console;
using Sshocked.Models;

namespace Sshocked.Services;

public static class HostVisuals
{
    public static string AuthBadge(ServerHost host, string theme = "Dark")
    {
        var palette = UiThemePalette.For(theme);
        var auth = host.AzureBastion is not null ? AuthType.AzureBastion : host.AuthType;
        var (icon, label, color) = auth switch
        {
            AuthType.Password => ("🔑", "Password", palette.Warning),
            AuthType.SshAgent => ("🤖", "Agent", palette.Accent),
            AuthType.CustomConfig => ("⚙", "Custom", palette.Muted),
            AuthType.AzureBastion => ("☁", "Azure Bastion", palette.Accent),
            _ => ("🔐", "SSH Key", palette.Success)
        };
        return $"[{color}]{icon} [[{label}]][/]";
    }

    public static string Alias(ServerHost host, string theme = "Dark")
        => (host.IsFavorite ? $"[{UiThemePalette.For(theme).Warning}]★[/] " : "") + $"[bold]{Markup.Escape(host.Alias)}[/]";

    public static string ReachabilityBadge(HostReachability status, string theme = "Dark")
    {
        var palette = UiThemePalette.For(theme);
        return status.State switch
        {
            HostReachabilityState.Online => $"[{palette.Success}]● online[/]",
            HostReachabilityState.Offline => $"[{palette.Error}]○ offline[/]",
            HostReachabilityState.Stale => $"[{palette.Warning}]◌ expired[/]",
            HostReachabilityState.Checking => $"[{palette.Accent}]… checking[/]",
            HostReachabilityState.ViaBastion => $"[{palette.Accent}]☁ via Bastion[/]",
            HostReachabilityState.ViaJumpHost => $"[{palette.Accent}]↗ via jump[/]",
            _ => $"[{palette.Muted}]— unknown[/]"
        };
    }

    public static int ContainerCount(ServerHost host)
        => host.SavedContainers.Concat(host.DockerInfo?.Containers ?? [])
            .Select(c => !string.IsNullOrWhiteSpace(c.ContainerId) ? c.ContainerId : c.Name)
            .Distinct(StringComparer.Ordinal).Count();

    public static string DockerBadge(ServerHost host, string theme = "Dark")
    {
        var palette = UiThemePalette.For(theme);
        var count = ContainerCount(host);
        if (count > 0) return $"[{palette.Accent}][[D:{count}]][/]";
        return host.DockerInfo?.DockerAvailable == true ? $"[{palette.Accent}][[D:0]][/]" : $"[{palette.Muted}]—[/]";
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
