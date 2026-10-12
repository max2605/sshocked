using Spectre.Console;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class HostVisualsTests
{
    [Fact]
    public void HostAndGroupMetadataIsEscapedAndDarkAndLightBadgesHaveValidMarkup()
    {
        var host = new ServerHost { Alias = "web[red]", IsFavorite = true };
        var alias = HostVisuals.Alias(host);
        Assert.Contains("web[[red]]", alias);
        foreach (var theme in new[] { "Dark", "Light" })
            foreach (var auth in Enum.GetValues<AuthType>())
            {
                host.AuthType = auth;
                Assert.NotNull(new Markup(HostVisuals.AuthBadge(host, theme)));
            }
    }

    [Fact]
    public void DockerCountDeduplicatesSavedAndScannedContainers()
    {
        var host = new ServerHost
        {
            SavedContainers = [new() { ContainerId = "one", Name = "one" }],
            DockerInfo = new() { DockerAvailable = true, Containers = [new() { ContainerId = "one", Name = "one" }, new() { ContainerId = "two", Name = "two" }] }
        };
        Assert.Equal(2, HostVisuals.ContainerCount(host));
        Assert.Contains("D:2", HostVisuals.DockerBadge(host));
    }

    [Fact]
    public void RelativeTimeLabelsHandleNeverMinutesHoursDaysAndFutureClockSkew()
    {
        var now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Never", HostVisuals.RelativeTime(null, now));
        Assert.Equal("Just now", HostVisuals.RelativeTime(now.AddMinutes(5), now));
        Assert.Equal("2m ago", HostVisuals.RelativeTime(now.AddMinutes(-2), now));
        Assert.Equal("3h ago", HostVisuals.RelativeTime(now.AddHours(-3), now));
        Assert.Equal("Yesterday", HostVisuals.RelativeTime(now.AddHours(-25), now));
        Assert.Equal("3d ago", HostVisuals.RelativeTime(now.AddDays(-3), now));
    }
}
