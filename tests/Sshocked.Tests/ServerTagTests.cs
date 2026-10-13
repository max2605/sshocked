using Microsoft.Extensions.DependencyInjection;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class ServerTagTests
{
    [Fact]
    public void TagNormalizationAndIntersectionsIgnoreCaseWhitespaceAndDuplicateSelections()
    {
        var tags = new ServerTagService();
        var hosts = new List<ServerHost>
        {
            new() { Id = "prod-web", Tags = [" Production ", "WEB"] },
            new() { Id = "prod-db", Tags = ["production", "database"] },
            new() { Id = "stage-web", Tags = ["staging", "web"] }
        };
        Assert.Equal(["Production", "web"], tags.Normalize(["Production", "production ", "web", "", "   "]));
        Assert.Equal(["prod-web"], tags.Filter(hosts, [" production", "web ", "WEB"]).Select(host => host.Id));
        Assert.Equal(3, tags.Filter(hosts, []).Count);
        Assert.Equal(4, tags.GetAll(hosts).Count);
        Assert.Empty(tags.Filter(hosts, ["missing"]));
    }

    [Fact]
    public async Task TagOperationsRefreshMembershipAcrossStaticGroupsAfterEachAction()
    {
        await using var fixture = new MenuWorkflowFixture(new()
        {
            Hosts = [new() { Id = "old", Alias = "old", GroupId = "a", Tags = ["prod"] },
                new() { Id = "new", Alias = "new", GroupId = "b", Tags = ["dev"] }]
        });
        fixture.Script.Choose("prod");
        fixture.Script.Choose("Run command", () => fixture.Repository.Update(config =>
        {
            config.Hosts[0].Tags = ["dev"];
            config.Hosts[1].Tags = ["PROD"];
        }));
        fixture.Script.Choose("Connect all");
        fixture.Script.Choose("Run macro");
        fixture.Script.Choose("Back");
        fixture.Script.Choose("Back");
        await fixture.Provider.GetRequiredService<ITagMenuService>().Show();
        Assert.Equal(["command", "connect", "macro"], fixture.Targets.Calls.Select(call => call.Action));
        Assert.Equal(["old"], fixture.Targets.Calls[0].HostIds);
        Assert.Equal(["new"], fixture.Targets.Calls[1].HostIds);
        Assert.Equal(["new"], fixture.Targets.Calls[2].HostIds);
        Assert.Contains(fixture.Script.Menus[1].Entries, entry => entry.Label == "Upload to matching servers");
        Assert.Contains(fixture.Script.Menus[1].Entries, entry => entry.Label == "Download from matching servers");
    }
}
